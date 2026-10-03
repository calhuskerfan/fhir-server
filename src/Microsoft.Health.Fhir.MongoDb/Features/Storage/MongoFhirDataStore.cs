// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.MongoDb.Configs;
using Microsoft.Health.Fhir.MongoDb.Extensions;
using Microsoft.Health.Fhir.MongoDb.Features.Search;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Identity.Client;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using static System.Net.Mime.MediaTypeNames;
using static DotLiquid.Variable;

namespace Microsoft.Health.Fhir.MongoDb.Features.Storage
{
    public sealed class MongoFhirDataStore : IFhirDataStore, IProvideCapability
    {
        private readonly ILogger<MongoFhirDataStore> _logger;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly IBundleOrchestrator _bundleOrchestrator;
        private readonly MongoDataStoreConfiguration _dataStoreConfiguration;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly IStorageDocumentBuilder _storageDocumentBuilder;

        public MongoFhirDataStore(
            ILogger<MongoFhirDataStore> logger,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            IBundleOrchestrator bundleOrchestrator,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            MongoDataStoreConfiguration dataStoreConfiguration,
            IStorageDocumentBuilder storageDocumentBuilder)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(coreFeatures, nameof(coreFeatures));
            EnsureArg.IsNotNull(bundleOrchestrator, nameof(bundleOrchestrator));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(storageDocumentBuilder, nameof(storageDocumentBuilder));
            EnsureArg.IsNotNull(dataStoreConfiguration, nameof(dataStoreConfiguration));

            _logger = logger;
            _requestContextAccessor = requestContextAccessor;
            _bundleOrchestrator = bundleOrchestrator;
            _dataStoreConfiguration = dataStoreConfiguration;
            _coreFeatures = coreFeatures.Value;
            _storageDocumentBuilder = storageDocumentBuilder;
        }

        public Task BulkUpdateSearchParameterIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        // Gets a list of of FHIR Resources by keys
        public async Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> keys, CancellationToken cancellationToken)
        {
            List<ResourceWrapper> listData = [];

            foreach (var item in keys)
            {
                ResourceWrapper rw = await GetAsync(item, cancellationToken);
                if (rw != null)
                {
                    listData.Add(rw);
                }
            }

            return listData.AsReadOnly();
        }

        // Gets a FHIR resource by its Resource Key
        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            var resourceType = key.ResourceType;
            var resourceId = key.Id;
            var isHistory = false;
            var isRawResourceMetaSet = true;

            var filter = Builders<BsonDocument>
                .Filter
                .Eq(FieldNameConstants.ResourceId, resourceId);

            filter = filter & Builders<BsonDocument>.Filter.Eq(FieldNameConstants.ResourceType, resourceType);

            if (string.IsNullOrEmpty(key.VersionId))
            {
                filter = filter & Builders<BsonDocument>.Filter.Eq(FieldNameConstants.IsLatest, true);
            }
            else
            {
                filter = filter & Builders<BsonDocument>.Filter.Eq(FieldNameConstants.Version, key.VersionId);
            }

            var document = await _dataStoreConfiguration
                .GetCollection()
                .Find(filter)
                .FirstOrDefaultAsync(cancellationToken);

            if (document == null)
            {
                return null;
            }

            return new ResourceWrapper(
                resourceId,
                document[FieldNameConstants.Version].ToString(),
                resourceType,
                new RawResource(
                    document[FieldNameConstants.Resource].ToJson(),
                    FhirResourceFormat.Json,
                    isMetaSet: isRawResourceMetaSet),
                null,
                DateTimeOffset.Now,
                document[FieldNameConstants.IsDeleted].AsBoolean,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: null)
            {
                IsHistory = isHistory,
            };
        }

        public Task<int?> GetProvisionedDataStoreCapacityAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// HardDeleteAsync
        /// </summary>
        /// <param name="key">resource key to delete</param>
        /// <param name="keepCurrentVersion">not implemented</param>
        /// <param name="cancellationToken">the async cancellation token</param>
        /// <returns>DeleteResult</returns>
        public async Task HardDeleteAsync(ResourceKey key, bool keepCurrentVersion, CancellationToken cancellationToken)
        {
            var resourceType = key.ResourceType;
            var resourceId = key.Id;

            var filter = Builders<BsonDocument>
                .Filter
                .Eq(FieldNameConstants.ResourceId, resourceId);

            filter = filter & Builders<BsonDocument>.Filter.Eq(FieldNameConstants.ResourceType, resourceType);

            var deleteResults = await _dataStoreConfiguration
                .GetCollection()
                .DeleteManyAsync(filter, cancellationToken);
        }

        public async Task<MergeOutcome> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, CancellationToken cancellationToken)
        {
            return await MergeAsync(resources, MergeOptions.Default, cancellationToken);
        }

        public async Task<MergeOutcome> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, MergeOptions mergeOptions, CancellationToken cancellationToken)
        {
            if (resources == null || resources.Count == 0)
            {
                return MergeOutcome.Empty;
            }

            var results = await MergeInternalAsync(
                resources,
                false,
                false,
                mergeOptions.EnlistInTransaction,
                false,
                cancellationToken); // TODO: Pass correct retries value once we start supporting retries

            return new MergeOutcome(MergeOutcomeFinalState.Completed, results);
        }

        // does the actual work of merging or creating new if the old one does not exist
        internal async Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeInternalAsync(
            IReadOnlyList<ResourceWrapperOperation> resources,
            bool keepLastUpdated,
            bool keepAllDeleted,
            bool enlistInTransaction,
            bool useReplicasForReads,
            CancellationToken cancellationToken)
        {
            var results = new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>();

            if (resources == null || resources.Count == 0)
            {
                return results;
            }

            var existingResources = (await GetAsync(resources.Select(r => r.Wrapper.ToResourceKey(true)).Distinct().ToList(), cancellationToken)).ToDictionary(r => r.ToResourceKey(true), r => r);

            foreach (var resourceExt in resources) // if list contains more that one version per resource it must be sorted by id and last updated DESC.
            {
                // Resource represents what we are trying to save to the datastore
                ResourceWrapper resource = resourceExt.Wrapper;
                DataStoreOperationIdentifier identifier = resourceExt.GetIdentifier();

                existingResources.TryGetValue(resource.ToResourceKey(true), out var existingResource);

                var nextVersion = existingResource == null ? 1 : GetNextVersion(existingResource);
                var versionId = nextVersion.ToString(CultureInfo.InvariantCulture);

                var previousLatestFilter = Builders<BsonDocument>
                    .Filter
                    .Eq(FieldNameConstants.ResourceId, resource.ResourceId);

                previousLatestFilter = previousLatestFilter & Builders<BsonDocument>.Filter.Eq(FieldNameConstants.ResourceType, resource.ResourceTypeName);
                previousLatestFilter = previousLatestFilter & Builders<BsonDocument>.Filter.Eq(FieldNameConstants.IsLatest, true);

                // update any exiting records to IsLatest = false.
                await _dataStoreConfiguration
                    .GetCollection()
                    .UpdateManyAsync(previousLatestFilter, Builders<BsonDocument>.Update.Set(FieldNameConstants.IsLatest, false), cancellationToken: cancellationToken);

                // ok, lets update the meta-data
                // TODOCJH:  Feels like we are duplicating here, but the metadata is natively sent in the response
                // where as the storage wrapper, version, property is not.
                var resourceDocument = UpdateResourceMetadata(
                    JObject.Parse(resourceExt.Wrapper.RawResource.Data),
                    resourceExt.Wrapper,
                    versionId);

                var doc = BuildStorageDocument(resource, versionId, resourceExt, resourceDocument);

                await _dataStoreConfiguration
                    .GetCollection()
                    .InsertOneAsync(doc, new InsertOneOptions(), cancellationToken);

                results.Add(
                    identifier,
                    new DataStoreOperationOutcome(new UpsertOutcome(resourceExt.Wrapper, existingResource == null ? SaveOutcomeType.Created : SaveOutcomeType.Updated)));
            }

            return results;
        }

        // set up to build the document to be stored in the database with DI so that we can
        // experiment with different storage document builders if we want to change the way we store the data in the future.
        // and align on search and sort.
        private BsonDocument BuildStorageDocument(
            ResourceWrapper resource,
            string versionId,
            ResourceWrapperOperation resourceExt,
            JObject resourceDocument)
        {
            return new StorageDocumentBuilder().BuildStorageDocument(resource, versionId, resourceExt, resourceDocument);
        }

        private static int GetNextVersion(ResourceWrapper existingResource)
        {
            if (existingResource == null)
            {
                return 1;
            }

            return int.TryParse(existingResource.Version, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
                ? version + 1
                : 1;
        }

        internal static JObject UpdateResourceMetadata(
            JObject resource,
            ResourceWrapper wrapper,
            string versionId)
        {
            if (resource == null || wrapper == null)
            {
                return resource;
            }

            var meta = resource["meta"] as JObject;
            if (meta == null)
            {
                meta = new JObject();
                resource["meta"] = meta;
            }

            meta["versionId"] = versionId;
            meta["lastUpdated"] = wrapper.LastModified.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture);

            return resource;
        }

        /// <summary>
        /// we can land here on a 'POST' a 'PUT' and 'DELETE'
        /// </summary>
        /// <param name="resource">Resource to upsert</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <returns>UpsertOutcome</returns>
        /// <exception cref="NotImplementedException">bundle operation not supported</exception>
        public async Task<UpsertOutcome> UpsertAsync(
            ResourceWrapperOperation resource,
            CancellationToken cancellationToken)
        {
            bool isBundleParallelOperation =
                _bundleOrchestrator.IsEnabled &&
                resource.BundleResourceContext != null &&
                resource.BundleResourceContext.IsParallelBundle;

            if (isBundleParallelOperation)
            {
                IBundleOrchestratorOperation operation = _bundleOrchestrator
                    .GetOperation(resource.BundleResourceContext.BundleOperationId);

                // BUGCJH: Not Quite Ready to turn on.
                throw new NotImplementedException();
            }

            _logger.LogInformation(resource.Wrapper.ResourceId);

            var mergeOutcome = await MergeAsync(new[] { resource }, cancellationToken);

            DataStoreOperationOutcome dataStoreOperationOutcome = mergeOutcome.Results.First().Value;

            if (dataStoreOperationOutcome.IsOperationSuccessful)
            {
                return dataStoreOperationOutcome.UpsertOutcome;
            }
            else
            {
                throw dataStoreOperationOutcome.Exception;
            }
        }

        // UpdateSearchParameterIndicesAsync
        public Task<ResourceWrapper> UpdateSearchParameterIndicesAsync(
            ResourceWrapper resourceWrapper,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        // HardDeleteAsync
        public async Task HardDeleteAsync(
            ResourceKey key,
            bool keepCurrentVersion,
            bool allowPartialSuccess,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(key, nameof(key));

            var filter = Builders<BsonDocument>
                .Filter
                .Eq($"{FieldNameConstants.Resource}.{FieldNameConstants.Id}", key.Id);

            await _dataStoreConfiguration
                .GetCollection()
                .DeleteOneAsync(filter, cancellationToken);
        }

        /// <summary>
        /// Builds the compatability statement for our FHIR server
        /// </summary>
        /// <param name="builder">builder</param>
        /// <param name="cancellationToken">cancellationToken</param>
        /// <returns>Task.CompletedTask</returns>
        public Task BuildAsync(
            ICapabilityStatementBuilder builder,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            _logger.LogDebug("MongoFhirDataStore. Building Capability Statement.");

            Stopwatch watch = Stopwatch.StartNew();

            try
            {
                builder = builder.PopulateDefaultResourceInteractions();
                _logger.LogDebug("MongoFhirDataStore. 'Default Resource Interactions' built. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "MongoFhirDataStore. 'Default Resource Interactions' failed. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                throw;
            }

            if (_coreFeatures.SupportsBatch)
            {
                try
                {
                    builder.AddGlobalInteraction(SystemRestfulInteraction.Batch);
                    _logger.LogDebug("MongoFhirDataStore. Add Batch Support built. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "MongoFhirDataStore. Configure SupportsBatch Failed");
                    throw;
                }
            }

            if (_coreFeatures.SupportsTransaction)
            {
                try
                {
                    builder.AddGlobalInteraction(SystemRestfulInteraction.Transaction);
                    _logger.LogDebug("MongoFhirDataStore. Add Transaction Support built. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "MongoFhirDataStore. Configure SupportsBatch Transaction");
                    throw;
                }
            }

            return Task.CompletedTask;
        }

        public Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
