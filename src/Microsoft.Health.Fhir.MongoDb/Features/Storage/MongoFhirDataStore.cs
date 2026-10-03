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
using Microsoft.Health.Fhir.Core.Exceptions;
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
        private const string ResourceVersionIndexName = "ux_resourceType_resourceId_version";
        private const int MaxVersionConflictAttempts = 20;

        private readonly ILogger<MongoFhirDataStore> _logger;
        private volatile bool _indexesEnsured;
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

        /// <summary>
        /// Updates the search indices of each resource in order. Updates already applied are kept if a later one fails
        /// (the first failure or cancellation propagates), so callers can safely re-run the batch.
        /// </summary>
        public async Task BulkUpdateSearchParameterIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resources, nameof(resources));

            foreach (var resource in resources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UpdateSearchParameterIndicesAsync(resource, cancellationToken);
            }
        }

        // Gets a list of of FHIR Resources by keys
        public async Task<IReadOnlyList<ResourceWrapper>> GetAsync(
            IReadOnlyList<ResourceKey> keys,
            CancellationToken cancellationToken)
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
        public async Task<ResourceWrapper> GetAsync(
            ResourceKey key,
            CancellationToken cancellationToken)
        {
            var resourceType = key.ResourceType;
            var resourceId = key.Id;

            var filter = Builders<BsonDocument>
                .Filter
                .Eq(FieldNameConstants.ResourceId, resourceId);

            if (string.IsNullOrEmpty(key.VersionId))
            {
                filter = filter & Builders<BsonDocument>.Filter.Eq(FieldNameConstants.IsLatest, true);
            }
            else
            {
                filter = filter & Builders<BsonDocument>.Filter.Eq(FieldNameConstants.Version, key.VersionId);
            }

            var documents = await _dataStoreConfiguration
                .GetCollection()
                .Find(filter)
                .ToListAsync(cancellationToken);

            // TODOCJH: Revisit how versions are handled.  The current implementation is a bit naive and assumes that the version is an integer and that it can be parsed as such.  This may not always be the case, so we should consider a more robust approach to handling versions.
            // Versions are strings;
            // pick the highest numeric version so a damaged state with several
            // latest documents still resolves deterministically.
            var document = documents
                .OrderByDescending(d => int.TryParse(d.GetValue(FieldNameConstants.Version, BsonNull.Value).ToString(), out var v) ? v : -1)
                .FirstOrDefault();

            if (document == null)
            {
                return null;
            }

            return FHIRMongoResourceWrapper.FromBsonDocument(document);
        }

        /// <summary>
        /// MongoDB has no provisioned-throughput equivalent, so capacity is reported as not configured (null).
        /// </summary>
        public Task<int?> GetProvisionedDataStoreCapacityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<int?>(null);
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

        /// <summary>
        /// does the actual work of merging or creating new if the old one does not exist
        /// </summary>
        /// <param name="resources">resources to be merged</param>
        /// <param name="keepLastUpdated">indicates whether to keep the last updated timestamp</param>
        /// <param name="keepAllDeleted">indicates whether to keep all deleted resources</param>
        /// <param name="enlistInTransaction">indicates whether to enlist in the current transaction</param>
        /// <param name="useReplicasForReads">indicates whether to use replica nodes for read operations</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <returns>A dictionary mapping operation identifiers to their outcomes</returns>
        /// <exception cref="InvalidOperationException">thrown if the merge is asked to enlist in a transaction but no MongoDB transaction is open</exception>
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

            await EnsureIndexesAsync(cancellationToken);
            var collection = _dataStoreConfiguration.GetCollection();

            // A failed write aborts a MongoDB transaction, so enlisted merges cannot retry version conflicts; they fail and roll back.
            IClientSessionHandle session = null;

            if (enlistInTransaction)
            {
                session = MongoDbTransactionScope.Current ?? throw new InvalidOperationException(
                    "The merge was asked to enlist in a transaction, but no MongoDB transaction is open. Begin one with ITransactionHandler.BeginTransaction().");
            }

            var existingResources = (await GetAsync(resources.Select(r => r.Wrapper.ToResourceKey(true)).Distinct().ToList(), cancellationToken)).ToDictionary(r => r.ToResourceKey(true), r => r);

            foreach (var resourceExt in resources) // if list contains more that one version per resource it must be sorted by id and last updated DESC.
            {
                // Resource represents what we are trying to save to the datastore
                ResourceWrapper resource = resourceExt.Wrapper;
                DataStoreOperationIdentifier identifier = resourceExt.GetIdentifier();

                var resourceKey = resource.ToResourceKey(true);

                // Operations for the same resource run sequentially in input order, so a write earlier in this
                // batch supersedes the snapshot taken before the loop.
                if (!existingResources.TryGetValue(resourceKey, out var existingResource))
                {
                    existingResource = null;
                }

                var nextVersion = existingResource == null ? 1 : GetNextVersion(existingResource);

                for (int attempt = 1; ; attempt++)
                {
                    var versionId = nextVersion.ToString(CultureInfo.InvariantCulture);

                    // ok, lets update the meta-data
                    // TODOCJH:  Feels like we are duplicating here, but the metadata is natively sent in the response
                    // where as the storage wrapper, version, property is not.
                    var resourceDocument = UpdateResourceMetadata(
                        JObject.Parse(resourceExt.Wrapper.RawResource.Data),
                        resourceExt.Wrapper,
                        versionId);

                    var doc = BuildStorageDocument(resource, versionId, resourceExt, resourceDocument);

                    try
                    {
                        await InsertAsync(collection, session, doc, cancellationToken);
                        resource.Version = versionId;
                        await ReconcileLatestAsync(collection, session, resource.ResourceTypeName, resource.ResourceId, cancellationToken);
                        break;
                    }
                    catch (MongoWriteException ex) when (session == null && ex.WriteError?.Category == ServerErrorCategory.DuplicateKey && attempt < MaxVersionConflictAttempts)
                    {
                        // Another writer claimed this version first. Re-derive the next version from everything stored.
                        nextVersion = await GetHighestVersionAsync(collection, session, resource.ResourceTypeName, resource.ResourceId, cancellationToken) + 1;
                    }
                }

                existingResources[resourceKey] = resource;

                results.Add(
                    identifier,
                    new DataStoreOperationOutcome(new UpsertOutcome(resourceExt.Wrapper, existingResource == null ? SaveOutcomeType.Created : SaveOutcomeType.Updated)));
            }

            return results;
        }

        // set up to build the document to be stored in the database with DI so that we can
        // experiment with different storage document builders
        // if we want to change the way we store the data in the future.
        // and align on search and sort.
        private BsonDocument BuildStorageDocument(
            ResourceWrapper resource,
            string versionId,
            ResourceWrapperOperation resourceExt,
            JObject resourceDocument)
        {
            return _storageDocumentBuilder.BuildStorageDocument(
                resource,
                versionId,
                resourceExt,
                resourceDocument);
        }

        // A unique (type, id, version) index is the concurrency guard: two writers can never persist the same version.
        private async Task EnsureIndexesAsync(CancellationToken cancellationToken)
        {
            if (_indexesEnsured)
            {
                return;
            }

            var keys = Builders<BsonDocument>.IndexKeys
                .Ascending(FieldNameConstants.ResourceType)
                .Ascending(FieldNameConstants.ResourceId)
                .Ascending(FieldNameConstants.Version);

            await _dataStoreConfiguration
                .GetCollection()
                .Indexes
                .CreateOneAsync(
                    new CreateIndexModel<BsonDocument>(keys, new CreateIndexOptions { Unique = true, Name = ResourceVersionIndexName }),
                    cancellationToken: cancellationToken);

            _indexesEnsured = true;
        }

        // TODOCJH:  We should consider a more robust approach to handling this in the future.
        // Makes the highest stored version the only latest document. Idempotent, so concurrent writers converge
        // regardless of how their reconciliations interleave.
        private static async Task ReconcileLatestAsync(
            IMongoCollection<BsonDocument> collection,
            IClientSessionHandle session,
            string resourceType,
            string resourceId,
            CancellationToken cancellationToken)
        {
            var filter = Builders<BsonDocument>.Filter.Eq(FieldNameConstants.ResourceId, resourceId) &
                Builders<BsonDocument>.Filter.Eq(FieldNameConstants.ResourceType, resourceType);

            var documents = await FindWith(collection, session, filter)
                .Project(Builders<BsonDocument>.Projection.Include(FieldNameConstants.Version).Include(FieldNameConstants.IsLatest))
                .ToListAsync(cancellationToken);

            if (documents.Count == 0)
            {
                return;
            }

            int ParseVersion(BsonDocument d) =>
                int.TryParse(d[FieldNameConstants.Version].AsString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

            var highest = documents.OrderByDescending(ParseVersion).First();

            var staleIds = documents
                .Where(d => d["_id"] != highest["_id"] && d.GetValue(FieldNameConstants.IsLatest, false).ToBoolean())
                .Select(d => d["_id"])
                .ToList();

            if (staleIds.Count > 0)
            {
                var staleFilter = Builders<BsonDocument>.Filter.In("_id", staleIds);
                var clearLatest = Builders<BsonDocument>.Update.Set(FieldNameConstants.IsLatest, false);
                if (session == null)
                {
                    await collection.UpdateManyAsync(staleFilter, clearLatest, cancellationToken: cancellationToken);
                }
                else
                {
                    await collection.UpdateManyAsync(session, staleFilter, clearLatest, cancellationToken: cancellationToken);
                }
            }

            if (!highest.GetValue(FieldNameConstants.IsLatest, false).ToBoolean())
            {
                var highestFilter = Builders<BsonDocument>.Filter.Eq("_id", highest["_id"]);
                var setLatest = Builders<BsonDocument>.Update.Set(FieldNameConstants.IsLatest, true);
                if (session == null)
                {
                    await collection.UpdateOneAsync(highestFilter, setLatest, cancellationToken: cancellationToken);
                }
                else
                {
                    await collection.UpdateOneAsync(session, highestFilter, setLatest, cancellationToken: cancellationToken);
                }
            }
        }

        private static async Task<int> GetHighestVersionAsync(
            IMongoCollection<BsonDocument> collection,
            IClientSessionHandle session,
            string resourceType,
            string resourceId,
            CancellationToken cancellationToken)
        {
            var filter = Builders<BsonDocument>.Filter.Eq(FieldNameConstants.ResourceId, resourceId) &
                Builders<BsonDocument>.Filter.Eq(FieldNameConstants.ResourceType, resourceType);

            var versions = await FindWith(collection, session, filter)
                .Project(Builders<BsonDocument>.Projection.Include(FieldNameConstants.Version))
                .ToListAsync(cancellationToken);

            return versions
                .Select(d => int.TryParse(d[FieldNameConstants.Version].AsString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0)
                .DefaultIfEmpty(0)
                .Max();
        }

        private static IFindFluent<BsonDocument, BsonDocument> FindWith(
            IMongoCollection<BsonDocument> collection,
            IClientSessionHandle session,
            FilterDefinition<BsonDocument> filter)
        {
            return session == null ? collection.Find(filter) : collection.Find(session, filter);
        }

        private static Task InsertAsync(
            IMongoCollection<BsonDocument> collection,
            IClientSessionHandle session,
            BsonDocument document,
            CancellationToken cancellationToken)
        {
            return session == null
                ? collection.InsertOneAsync(document, new InsertOneOptions(), cancellationToken)
                : collection.InsertOneAsync(session, document, new InsertOneOptions(), cancellationToken);
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

                // The operation collects the bundle's resources and calls MergeAsync once all have arrived.
                return await operation.AppendResourceAsync(resource, this, cancellationToken).ConfigureAwait(false);
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

        /// <summary>
        /// Replaces only the search indexes (and search parameter hash) of the stored version identified by the wrapper,
        /// leaving the resource body, version, latest flag and history untouched.
        /// </summary>
        public async Task<ResourceWrapper> UpdateSearchParameterIndicesAsync(
            ResourceWrapper resourceWrapper,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            var indexes = new BsonArray();
            foreach (var entry in resourceWrapper.SearchIndices ?? Array.Empty<SearchIndexEntry>())
            {
                indexes.Add(SearchIndexEntryBsonDocumentGenerator.Generate(entry));
            }

            var filter = Builders<BsonDocument>.Filter.Eq(FieldNameConstants.ResourceType, resourceWrapper.ResourceTypeName) &
                Builders<BsonDocument>.Filter.Eq(FieldNameConstants.ResourceId, resourceWrapper.ResourceId) &
                Builders<BsonDocument>.Filter.Eq(FieldNameConstants.Version, resourceWrapper.Version);

            var update = Builders<BsonDocument>.Update.Set(FieldNameConstants.SearchIndexes, indexes);
            if (resourceWrapper.SearchParameterHash != null)
            {
                update = update.Set(FieldNameConstants.SearchParameterHash, resourceWrapper.SearchParameterHash);
            }

            var result = await _dataStoreConfiguration
                .GetCollection()
                .UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

            if (result.MatchedCount == 0)
            {
                throw new ResourceNotFoundException(
                    $"Resource {resourceWrapper.ResourceTypeName}/{resourceWrapper.ResourceId} version {resourceWrapper.Version} was not found.",
                    resourceWrapper.ToResourceKey(true));
            }

            return resourceWrapper;
        }

        /// <summary>
        /// Hard deletes every stored version of a resource identified by type and id.
        /// When <paramref name="keepCurrentVersion"/> is true the latest version is retained.
        /// A single DeleteMany is used, so a failure surfaces as an exception;
        /// <paramref name="allowPartialSuccess"/> has no additional effect.
        /// </summary>
        public async Task HardDeleteAsync(
            ResourceKey key,
            bool keepCurrentVersion,
            bool allowPartialSuccess,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(key, nameof(key));

            var builder = Builders<BsonDocument>.Filter;
            var filter = builder.Eq(FieldNameConstants.ResourceType, key.ResourceType)
                & builder.Eq(FieldNameConstants.ResourceId, key.Id);

            if (keepCurrentVersion)
            {
                filter &= builder.Ne(FieldNameConstants.IsLatest, true);
            }

            await _dataStoreConfiguration
                .GetCollection()
                .DeleteManyAsync(filter, cancellationToken);
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

            if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                _logger.LogDebug("MongoFhirDataStore. Building Capability Statement.");
            }

            Stopwatch watch = Stopwatch.StartNew();

            try
            {
                builder = builder.PopulateDefaultResourceInteractions();
                if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
                {
                    _logger.LogDebug("MongoFhirDataStore. 'Default Resource Interactions' built. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                }
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
                    if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
                    {
                        _logger.LogDebug("MongoFhirDataStore. Add Batch Support built. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                    }
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
                    if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
                    {
                        _logger.LogDebug("MongoFhirDataStore. Add Transaction Support built. Elapsed: {ElapsedTime}. Memory: {MemoryInUse}.", watch.Elapsed, GC.GetTotalMemory(forceFullCollection: false));
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "MongoFhirDataStore. Configure SupportsBatch Transaction");
                    throw;
                }
            }

            return Task.CompletedTask;
        }

        public Task TryLogEvent(
            string process,
            string status,
            string text,
            DateTime? startDate,
            CancellationToken cancellationToken)
        {
            // TODOCJH: Implement logging to MongoDB for events. For now, this method does nothing.
            // Do a little more looking into this, but for now, we will just return a completed task.
            return Task.CompletedTask;
        }
    }
}
