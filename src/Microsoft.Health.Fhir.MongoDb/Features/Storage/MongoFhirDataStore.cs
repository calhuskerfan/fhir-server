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

        public MongoFhirDataStore(
            ILogger<MongoFhirDataStore> logger,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            IBundleOrchestrator bundleOrchestrator,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            MongoDataStoreConfiguration dataStoreConfiguration)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(coreFeatures, nameof(coreFeatures));
            EnsureArg.IsNotNull(bundleOrchestrator, nameof(bundleOrchestrator));
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(dataStoreConfiguration, nameof(dataStoreConfiguration));

            _logger = logger;
            _requestContextAccessor = requestContextAccessor;
            _bundleOrchestrator = bundleOrchestrator;
            _dataStoreConfiguration = dataStoreConfiguration;
            _coreFeatures = coreFeatures.Value;
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
            var version = 1;
            var isHistory = false;
            var isRawResourceMetaSet = true;

            /*
            TODO:  Need to review these
            var searchParamHash = reader.Read(VLatest.Resource.SearchParamHash, 8);
            var requestMethod = readRequestMethod ? reader.Read(VLatest.Resource.RequestMethod, 9) : null;
            var resourceSurrogateId = 5108658606258160320;
            var rawResourceBytes = reader.GetSqlBytes(6).Value;
            var resourceTypeId = 103;
            */

            // set up the filters
            // TODOCJH:  Should we also filter on resource type,
            // the resource.id should be sufficient and unique across the system
            var filter = Builders<BsonDocument>
                .Filter
                .Eq("resource.id", resourceId);

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
                version.ToString(CultureInfo.InvariantCulture),
                key.ResourceType,
                new RawResource(
                    document[FieldNameConstants.Resource].ToString(),
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
                .Eq($"{FieldNameConstants.Resource}.{FieldNameConstants.Id}", resourceId);

            var deleteResults = await _dataStoreConfiguration
                .GetCollection()
                .DeleteOneAsync(filter, cancellationToken);
        }

        public async Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeAsync(
            IReadOnlyList<ResourceWrapperOperation> resources,
            CancellationToken cancellationToken)
        {
            return await MergeAsync(resources, MergeOptions.Default, cancellationToken);
        }

        public async Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeAsync(
            IReadOnlyList<ResourceWrapperOperation> resources,
            MergeOptions mergeOptions,
            CancellationToken cancellationToken)
        {
            var results = await MergeInternalAsync(
                resources,
                false,
                false,
                mergeOptions.EnlistInTransaction,
                false,
                cancellationToken); // TODO: Pass correct retries value once we start supporting retries

            return results;
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
                ResourceWrapper resource = resourceExt.Wrapper;
                DataStoreOperationIdentifier identifier = resourceExt.GetIdentifier();

                existingResources.TryGetValue(resource.ToResourceKey(true), out var existingResource);

                if (existingResource == null)
                {
                    var doc = new JObject
                    {
                        {
                            FieldNameConstants.Resource,
                            JToken.Parse(resourceExt.Wrapper.RawResource.Data)
                        },
                    };

                    BsonDocument document = doc.ToBsonDocument();

                    document.Add(FieldNameConstants.IsDeleted, false);

                    document.AddRange(new BsonDocument(
                        FieldNameConstants.SearchIndexes,
                        GetSearchIndexes(resourceExt.Wrapper.SearchIndices)));

                    await _dataStoreConfiguration
                        .GetCollection()
                        .InsertOneAsync(document, new InsertOneOptions(), cancellationToken);

                    results.Add(
                        identifier,
                        new DataStoreOperationOutcome(new UpsertOutcome(resourceExt.Wrapper, SaveOutcomeType.Created)));
                }
                else
                {
                    // TODOCJH: Implement Update
                    // ok, we are updating an existing resource
                    // for now we are just going to flush and fill the resource and search indexes

                    string text = resourceExt.Wrapper.RawResource.Data;

                    FilterDefinition<BsonDocument> filter = Builders<BsonDocument>
                        .Filter
                        .Eq($"{FieldNameConstants.Resource}.{FieldNameConstants.Id}", resource.ResourceId);

                    UpdateDefinition<BsonDocument> update = Builders<BsonDocument>.Update
                        .Set(FieldNameConstants.Resource, JObject.Parse(text).ToBsonDocument())
                        .Set(FieldNameConstants.SearchIndexes, GetSearchIndexes(resourceExt.Wrapper.SearchIndices))
                        .Set(FieldNameConstants.IsDeleted, resourceExt.Wrapper.IsDeleted);

                    UpdateResult updateResult = await _dataStoreConfiguration
                        .GetCollection()
                        .UpdateOneAsync(filter, update, null, cancellationToken);

                    results.Add(
                        identifier,
                        new DataStoreOperationOutcome(new UpsertOutcome(resourceExt.Wrapper, SaveOutcomeType.Updated)));
                }
            }

            return results;
        }

        private BsonArray GetSearchIndexes(IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            BsonArray indexes = new BsonArray();

            foreach (SearchIndexEntry entry in searchIndices)
            {
                indexes.Add(SearchIndexEntryBsonDocumentGenerator.Generate(entry));
            }

            return indexes;
        }

        /// <summary>
        /// we can land here on a 'POST' a 'PUT' and 'DELETE'
        /// </summary>
        /// <param name="resource">Resource to upsert</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <returns>UpsertOutcome</returns>
        /// <exception cref="NotImplementedException">bundle operation not supported</exception>
        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapperOperation resource, CancellationToken cancellationToken)
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

            DataStoreOperationOutcome dataStoreOperationOutcome = mergeOutcome.First().Value;

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
        public Task HardDeleteAsync(
            ResourceKey key,
            bool keepCurrentVersion,
            bool allowPartialSuccess,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
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
    }
}
