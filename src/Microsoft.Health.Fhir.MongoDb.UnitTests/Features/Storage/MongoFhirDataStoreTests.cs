// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.MongoDb.Configs;
using Microsoft.Health.Fhir.MongoDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Testcontainers.MongoDb;
using Xunit;

namespace Microsoft.Health.Fhir.MongoDb.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public sealed class MongoFhirDataStoreTests : IClassFixture<MongoDbTestFixture>
    {
        private readonly MongoDbTestFixture _fixture;

        public MongoFhirDataStoreTests(MongoDbTestFixture fixture)
        {
            _fixture = fixture;
            ModelInfoProvider.SetProvider(MockModelInfoProviderBuilder.Create(FhirSpecification.R4).Build());
        }

        [Fact]
        public async Task GetAsync_FiltersByResourceTypeAndVersion_AndReturnsNullForMissingResource()
        {
            await _fixture.ResetAsync();
            await _fixture.SeedAsync("Patient", "shared-id", "1", isLatest: false);
            await _fixture.SeedAsync("Patient", "shared-id", "2", isLatest: true);
            await _fixture.SeedAsync("Observation", "shared-id", "1", isLatest: true);

            var dataStore = CreateDataStore();

            var current = await dataStore.GetAsync(new ResourceKey("Patient", "shared-id"), CancellationToken.None);
            var historical = await dataStore.GetAsync(new ResourceKey("Patient", "shared-id", "1"), CancellationToken.None);
            var missing = await dataStore.GetAsync(new ResourceKey("Patient", "missing"), CancellationToken.None);

            Assert.Equal("2", current.Version);
            Assert.Equal("1", historical.Version);
            Assert.Null(missing);
        }

        [Fact]
        public async Task GetAsync_RestoresPersistedMetadataForHistoryAndDeletedResource()
        {
            await _fixture.ResetAsync();
            var lastModified = new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero);
            await _fixture.SeedAsync("Patient", "patient-1", "7", isLatest: false, isDeleted: true, lastModified);

            var wrapper = await CreateDataStore().GetAsync(
                new ResourceKey("Patient", "patient-1", "7"),
                CancellationToken.None);

            Assert.Equal(lastModified, wrapper.LastModified);
            Assert.Equal("7", wrapper.Version);
            Assert.True(wrapper.IsHistory);
            Assert.True(wrapper.IsDeleted);

            using var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(wrapper.RawResource.Data))
            {
                DateParseHandling = Newtonsoft.Json.DateParseHandling.None,
            };
            var resource = JObject.Load(reader);
            Assert.Equal("7", resource["meta"]?["versionId"]?.Value<string>());
            Assert.Equal("2024-03-04T05:06:07Z", resource["meta"]?["lastUpdated"]?.Value<string>());
        }

        [Fact]
        public async Task GetAsync_LegacyDocumentMissingOptionalFields_UsesDefaults()
        {
            await _fixture.ResetAsync();
            await _fixture.InsertAsync(new BsonDocument
            {
                { "resourceId", "legacy-1" },
                { "resourceType", "Patient" },
                { "isLatest", true },
                {
                    "resource",
                    new BsonDocument
                    {
                        { "resourceType", "Patient" },
                        { "id", "legacy-1" },
                        { "meta", new BsonDocument("versionId", "4") },
                    }
                },
            });

            var wrapper = await CreateDataStore().GetAsync(new ResourceKey("Patient", "legacy-1"), CancellationToken.None);

            Assert.Equal("4", wrapper.Version);
            Assert.False(wrapper.IsDeleted);
            Assert.False(wrapper.IsHistory);
        }

        [Fact]
        public async Task GetAsync_DocumentMissingResourceBody_ThrowsInvalidOperation()
        {
            await _fixture.ResetAsync();
            await _fixture.InsertAsync(new BsonDocument
            {
                { "resourceId", "broken-1" },
                { "resourceType", "Patient" },
                { "isLatest", true },
            });

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateDataStore().GetAsync(new ResourceKey("Patient", "broken-1"), CancellationToken.None));
        }

        [Fact]
        public async Task MergeAsync_CreateThenUpdate_AssignsIncreasingVersionsAndOneLatestVersion()
        {
            await _fixture.ResetAsync();
            var dataStore = CreateDataStore();

            await dataStore.MergeAsync(new[] { CreateOperation("patient-1", inputVersion: "incoming-1") }, CancellationToken.None);
            await dataStore.MergeAsync(new[] { CreateOperation("patient-1", inputVersion: "incoming-2") }, CancellationToken.None);

            var documents = await _fixture.GetResourceDocumentsAsync("Patient", "patient-1");

            Assert.Equal(new[] { "1", "2" }, documents.Select(document => document[FieldNameConstants.Version].AsString).OrderBy(version => version));
            Assert.Single(documents, document => document[FieldNameConstants.IsLatest].AsBoolean);
            Assert.Equal("2", (await dataStore.GetAsync(new ResourceKey("Patient", "patient-1"), CancellationToken.None)).Version);
        }

        [Fact]
        public async Task MergeAsync_KeepsIdentityAndVersionConsistentAcrossWrapperDocumentAndBody()
        {
            await _fixture.ResetAsync();
            var dataStore = CreateDataStore();
            var operation = CreateOperation("patient-1", inputVersion: "incoming-1");

            await dataStore.MergeAsync(new[] { operation }, CancellationToken.None);
            var outcomes = await dataStore.MergeAsync(new[] { CreateOperation("patient-1", inputVersion: "incoming-2") }, CancellationToken.None);

            var returned = outcomes.Results.Single().Value.UpsertOutcome.Wrapper;
            var document = (await _fixture.GetResourceDocumentsAsync("Patient", "patient-1")).Single(d => d[FieldNameConstants.IsLatest].AsBoolean);
            var body = document[FieldNameConstants.Resource].AsBsonDocument;

            Assert.Equal("2", returned.Version);
            Assert.Equal("2", document[FieldNameConstants.Version].AsString);
            Assert.Equal("2", body["meta"]["versionId"].AsString);
            Assert.Equal(document[FieldNameConstants.ResourceId].AsString, body["id"].AsString);
            Assert.Equal(document[FieldNameConstants.ResourceType].AsString, body["resourceType"].AsString);
        }

        [Fact]
        public async Task MergeAsync_RepeatedResourceOperationsInOneBatch_AssignsSuccessiveVersionsInInputOrder()
        {
            await _fixture.ResetAsync();
            var dataStore = CreateDataStore();

            await dataStore.MergeAsync(
                new[]
                {
                    CreateOperation("patient-1", inputVersion: "incoming-1", name: "first"),
                    CreateOperation("patient-1", inputVersion: "incoming-2", name: "second"),
                },
                CancellationToken.None);

            var documents = await _fixture.GetResourceDocumentsAsync("Patient", "patient-1");

            Assert.Equal(new[] { "1", "2" }, documents.Select(document => document[FieldNameConstants.Version].AsString).OrderBy(version => version));
            Assert.Single(documents, document => document[FieldNameConstants.IsLatest].AsBoolean);

            var latest = await CreateDataStore().GetAsync(new ResourceKey("Patient", "patient-1"), CancellationToken.None);
            Assert.Equal("second", JObject.Parse(latest.RawResource.Data)["name"]?.Value<string>());
        }

        [Fact]
        public async Task MergeAsync_InterleavedRepeatedKeysAcrossExistingResources_LeavesOneLatestPerResource()
        {
            await _fixture.ResetAsync();
            await _fixture.SeedAsync("Patient", "patient-1", "1", isLatest: true);
            var dataStore = CreateDataStore();

            await dataStore.MergeAsync(
                new[]
                {
                    CreateOperation("patient-1", inputVersion: "a", name: "p1-v2"),
                    CreateOperation("patient-2", inputVersion: "b", name: "p2-v1"),
                    CreateOperation("patient-1", inputVersion: "c", name: "p1-v3"),
                    CreateOperation("patient-2", inputVersion: "d", name: "p2-v2"),
                    CreateOperation("patient-1", inputVersion: "e", name: "p1-v4"),
                },
                CancellationToken.None);

            foreach (var (id, expectedVersions) in new[] { ("patient-1", new[] { "1", "2", "3", "4" }), ("patient-2", new[] { "1", "2" }) })
            {
                var documents = await _fixture.GetResourceDocumentsAsync("Patient", id);

                Assert.Equal(expectedVersions, documents.Select(d => d[FieldNameConstants.Version].AsString).OrderBy(v => v));
                var latest = Assert.Single(documents, d => d[FieldNameConstants.IsLatest].AsBoolean);
                Assert.Equal(expectedVersions.Last(), latest[FieldNameConstants.Version].AsString);
            }
        }

        [Fact]
        public async Task MergeAsync_ConcurrentWritersForSameResource_ProduceUniqueVersionsAndOneLatest()
        {
            await _fixture.ResetAsync();
            await _fixture.SeedAsync("Patient", "patient-1", "1", isLatest: true);

            const int writers = 8;
            await Task.WhenAll(Enumerable.Range(0, writers).Select(i =>
                Task.Run(() => CreateDataStore().MergeAsync(new[] { CreateOperation("patient-1", name: $"w{i}") }, CancellationToken.None))));

            var documents = await _fixture.GetResourceDocumentsAsync("Patient", "patient-1");

            Assert.Equal(
                Enumerable.Range(1, writers + 1).Select(v => v.ToString()),
                documents.Select(d => d[FieldNameConstants.Version].AsString).OrderBy(v => int.Parse(v)));
            var latest = Assert.Single(documents, d => d[FieldNameConstants.IsLatest].AsBoolean);
            Assert.Equal((writers + 1).ToString(), latest[FieldNameConstants.Version].AsString);
        }

        [Fact]
        public async Task MergeAsync_CreatesUniqueVersionIndex_SoDuplicateVersionsAreRejected()
        {
            await _fixture.ResetAsync();
            await CreateDataStore().MergeAsync(new[] { CreateOperation("patient-1") }, CancellationToken.None);

            await Assert.ThrowsAsync<MongoWriteException>(() => _fixture.SeedAsync("Patient", "patient-1", "1", isLatest: false));
        }

        [Fact]
        public async Task MergeAsync_SameIdDifferentResourceType_DoesNotDemoteOtherTypeLatest()
        {
            await _fixture.ResetAsync();
            await _fixture.SeedAsync("Observation", "shared-1", "1", isLatest: true);
            var dataStore = CreateDataStore();

            await dataStore.MergeAsync(new[] { CreateOperation("shared-1") }, CancellationToken.None);

            var observation = await _fixture.GetResourceDocumentsAsync("Observation", "shared-1");
            Assert.True(Assert.Single(observation)[FieldNameConstants.IsLatest].AsBoolean);
        }

        [Fact]
        public async Task MergeAsync_WhenPreviousStateHasMultipleLatestDocuments_LeavesExactlyOneLatest()
        {
            await _fixture.ResetAsync();
            await _fixture.SeedAsync("Patient", "patient-1", "1", isLatest: true);
            await _fixture.SeedAsync("Patient", "patient-1", "2", isLatest: true);
            var dataStore = CreateDataStore();

            await dataStore.MergeAsync(new[] { CreateOperation("patient-1") }, CancellationToken.None);

            var documents = await _fixture.GetResourceDocumentsAsync("Patient", "patient-1");
            var latest = Assert.Single(documents, d => d[FieldNameConstants.IsLatest].AsBoolean);
            Assert.Equal("3", latest[FieldNameConstants.Version].AsString);
        }

        [Fact]
        public async Task HardDeleteAsync_KeepingCurrentVersion_DeletesHistoryForFullResourceIdentity()
        {
            await _fixture.ResetAsync();
            await _fixture.SeedAsync("Observation", "shared-id", "1", isLatest: true);
            await _fixture.SeedAsync("Patient", "shared-id", "1", isLatest: false);
            await _fixture.SeedAsync("Patient", "shared-id", "2", isLatest: true);

            await CreateDataStore().HardDeleteAsync(
                new ResourceKey("Patient", "shared-id"),
                keepCurrentVersion: true,
                allowPartialSuccess: false,
                CancellationToken.None);

            var patients = await _fixture.GetResourceDocumentsAsync("Patient", "shared-id");
            var observations = await _fixture.GetResourceDocumentsAsync("Observation", "shared-id");

            Assert.Single(patients);
            Assert.Equal("2", patients[0][FieldNameConstants.Version].AsString);
            Assert.True(patients[0][FieldNameConstants.IsLatest].AsBoolean);
            Assert.Single(observations);
        }

        [Fact]
        public async Task HardDeleteAsync_DeletingAllVersions_DoesNotDeleteDifferentResourceTypeWithSameId()
        {
            await _fixture.ResetAsync();
            await _fixture.SeedAsync("Observation", "shared-id", "1", isLatest: true);
            await _fixture.SeedAsync("Patient", "shared-id", "1", isLatest: false);
            await _fixture.SeedAsync("Patient", "shared-id", "2", isLatest: true);

            await CreateDataStore().HardDeleteAsync(
                new ResourceKey("Patient", "shared-id"),
                keepCurrentVersion: false,
                allowPartialSuccess: true,
                CancellationToken.None);

            Assert.Empty(await _fixture.GetResourceDocumentsAsync("Patient", "shared-id"));
            Assert.Single(await _fixture.GetResourceDocumentsAsync("Observation", "shared-id"));
        }

        [Fact]
        public async Task UpdateSearchParameterIndicesAsync_PersistsIndexesAndPreservesStoredResourceMetadata()
        {
            await _fixture.ResetAsync();
            await _fixture.SeedAsync("Patient", "patient-1", "4", isLatest: true, additionalField: "preserve-me");
            var index = CreateSearchIndex("name", "Contoso");
            var wrapper = CreateWrapper("patient-1", version: "4", searchIndices: new[] { index });

            await CreateDataStore().UpdateSearchParameterIndicesAsync(wrapper, CancellationToken.None);

            var document = Assert.Single(await _fixture.GetResourceDocumentsAsync("Patient", "patient-1"));
            Assert.Equal("4", document[FieldNameConstants.Version].AsString);
            Assert.True(document[FieldNameConstants.IsLatest].AsBoolean);
            Assert.Equal("preserve-me", document["additionalField"].AsString);
            Assert.Equal("name", document[FieldNameConstants.SearchIndexes][0]["SearchParameter"]["Code"].AsString);
        }

        [Fact]
        public async Task BulkUpdateSearchParameterIndicesAsync_UpdatesEachResource()
        {
            await _fixture.ResetAsync();
            await _fixture.SeedAsync("Patient", "patient-1", "1", isLatest: true);
            await _fixture.SeedAsync("Patient", "patient-2", "2", isLatest: true);

            await CreateDataStore().BulkUpdateSearchParameterIndicesAsync(
                new[]
                {
                    CreateWrapper("patient-1", version: "1", searchIndices: new[] { CreateSearchIndex("name", "First") }),
                    CreateWrapper("patient-2", version: "2", searchIndices: new[] { CreateSearchIndex("name", "Second") }),
                },
                CancellationToken.None);

            var patient1 = Assert.Single(await _fixture.GetResourceDocumentsAsync("Patient", "patient-1"));
            var patient2 = Assert.Single(await _fixture.GetResourceDocumentsAsync("Patient", "patient-2"));
            Assert.Equal("First", patient1[FieldNameConstants.SearchIndexes][0]["Value"]["String"].AsString);
            Assert.Equal("Second", patient2[FieldNameConstants.SearchIndexes][0]["Value"]["String"].AsString);
        }

        [Fact]
        public async Task GetAsync_WithCanceledToken_PropagatesCancellation()
        {
            await _fixture.ResetAsync();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateDataStore().GetAsync(new ResourceKey("Patient", "patient-1"), cancellation.Token));
        }

        [Fact]
        public async Task GetAsync_WhenMongoDbCannotBeReached_PropagatesDriverFailure()
        {
            var dataStore = CreateDataStore(connectionString: "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100");

            // The driver reports server-selection failures as TimeoutException.
            var exception = await Record.ExceptionAsync(
                () => dataStore.GetAsync(new ResourceKey("Patient", "patient-1"), CancellationToken.None));

            Assert.True(exception is MongoException || exception is TimeoutException, $"Unexpected: {exception?.GetType()}");
        }

        [Fact]
        public async Task MergeAsync_WithEnlistment_RollsBackWhenAnOperationFails()
        {
            await _fixture.ResetAsync();
            var builder = new FailingOnSecondStorageDocumentBuilder();
            var dataStore = CreateDataStore(builder);

            using (new MongoDbTransactionHandler(new MongoDataStoreConfiguration { ConnectionString = _fixture.ConnectionString }).BeginTransaction())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => dataStore.MergeAsync(
                        new[]
                        {
                            CreateOperation("patient-1"),
                            CreateOperation("patient-2"),
                        },
                        new MergeOptions(enlistTransaction: true),
                        CancellationToken.None));
            }

            Assert.Empty(await _fixture.GetResourceDocumentsAsync("Patient", "patient-1"));
            Assert.Empty(await _fixture.GetResourceDocumentsAsync("Patient", "patient-2"));
        }

        [Fact]
        public async Task MergeAsync_WithEnlistment_CommitsWhenScopeCompletes()
        {
            await _fixture.ResetAsync();
            var dataStore = CreateDataStore();

            using (var scope = new MongoDbTransactionHandler(new MongoDataStoreConfiguration { ConnectionString = _fixture.ConnectionString }).BeginTransaction())
            {
                await dataStore.MergeAsync(
                    new[] { CreateOperation("patient-1"), CreateOperation("patient-2") },
                    new MergeOptions(enlistTransaction: true),
                    CancellationToken.None);

                scope.Complete();
            }

            var document = Assert.Single(await _fixture.GetResourceDocumentsAsync("Patient", "patient-1"));
            Assert.True(document["isLatest"].AsBoolean);
            Assert.Single(await _fixture.GetResourceDocumentsAsync("Patient", "patient-2"));
        }

        [Fact]
        public async Task MergeAsync_WithEnlistment_RollsBackWhenScopeIsNotCompleted()
        {
            await _fixture.ResetAsync();
            var dataStore = CreateDataStore();

            using (new MongoDbTransactionHandler(new MongoDataStoreConfiguration { ConnectionString = _fixture.ConnectionString }).BeginTransaction())
            {
                await dataStore.MergeAsync(
                    new[] { CreateOperation("patient-1") },
                    new MergeOptions(enlistTransaction: true),
                    CancellationToken.None);
            }

            Assert.Empty(await _fixture.GetResourceDocumentsAsync("Patient", "patient-1"));
        }

        [Fact]
        public async Task MergeAsync_WithEnlistmentButNoOpenTransaction_Throws()
        {
            await _fixture.ResetAsync();
            var dataStore = CreateDataStore();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dataStore.MergeAsync(
                    new[] { CreateOperation("patient-1") },
                    new MergeOptions(enlistTransaction: true),
                    CancellationToken.None));

            Assert.Empty(await _fixture.GetResourceDocumentsAsync("Patient", "patient-1"));
        }

        [Fact]
        public async Task UpsertAsync_ParallelTransactionBundle_PersistsThroughBundleOperation()
        {
            await _fixture.ResetAsync();
            var logger = Substitute.For<ILogger<BundleOrchestrator>>();
            var operation = new BundleOrchestratorOperation(
                BundleOrchestratorOperationType.Transaction,
                "MongoDB datastore integration test",
                expectedNumberOfResources: 1,
                logger);
            var orchestrator = Substitute.For<IBundleOrchestrator>();
            orchestrator.IsEnabled.Returns(true);
            orchestrator.GetOperation(operation.Id).Returns(operation);
            var dataStore = CreateDataStore(bundleOrchestrator: orchestrator);
            var context = new BundleResourceContext(
                Bundle.BundleType.Transaction,
                BundleProcessingLogic.Parallel,
                Bundle.HTTPVerb.POST,
                "patient-1",
                operation.Id);

            var outcome = await dataStore.UpsertAsync(CreateOperation("patient-1", bundleResourceContext: context), CancellationToken.None);

            Assert.Equal(SaveOutcomeType.Created, outcome.OutcomeType);
            Assert.Single(await _fixture.GetResourceDocumentsAsync("Patient", "patient-1"));
        }

        [Fact]
        public async Task GetProvisionedDataStoreCapacityAsync_WhenNotConfigured_ReturnsNull()
        {
            await _fixture.ResetAsync();

            Assert.Null(await CreateDataStore().GetProvisionedDataStoreCapacityAsync(CancellationToken.None));
        }

        [Fact]
        public async Task MergeAsync_UsesInjectedStorageDocumentBuilder()
        {
            await _fixture.ResetAsync();
            var builder = new MarkedStorageDocumentBuilder();
            var dataStore = CreateDataStore(builder);

            await dataStore.MergeAsync(new[] { CreateOperation("patient-1") }, CancellationToken.None);

            var document = Assert.Single(await _fixture.GetResourceDocumentsAsync("Patient", "patient-1"));
            Assert.Equal(1, builder.CallCount);
            Assert.True(document["builderMarker"].AsBoolean);
        }

        private MongoFhirDataStore CreateDataStore(
            IStorageDocumentBuilder? storageDocumentBuilder = null,
            IBundleOrchestrator? bundleOrchestrator = null,
            string? connectionString = null)
        {
            return new MongoFhirDataStore(
                Substitute.For<ILogger<MongoFhirDataStore>>(),
                Substitute.For<RequestContextAccessor<IFhirRequestContext>>(),
                bundleOrchestrator ?? Substitute.For<IBundleOrchestrator>(),
                Options.Create(new CoreFeatureConfiguration()),
                new MongoDataStoreConfiguration { ConnectionString = connectionString ?? _fixture.ConnectionString },
                storageDocumentBuilder ?? new StorageDocumentBuilder());
        }

        private static ResourceWrapperOperation CreateOperation(
            string id,
            string? inputVersion = null,
            string name = "test",
            BundleResourceContext? bundleResourceContext = null,
            bool deleted = false)
        {
            return new ResourceWrapperOperation(
                CreateWrapper(id, inputVersion, name, deleted: deleted),
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext);
        }

        private static ResourceWrapper CreateWrapper(
            string id,
            string? version = null,
            string name = "test",
            IReadOnlyCollection<SearchIndexEntry>? searchIndices = null,
            bool deleted = false)
        {
            var lastModified = new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero);
            var resource = new JObject
            {
                ["resourceType"] = "Patient",
                ["id"] = id,
                ["name"] = name,
                ["meta"] = new JObject
                {
                    ["versionId"] = version,
                    ["lastUpdated"] = lastModified.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                },
            };

            return new ResourceWrapper(
                id,
                version,
                "Patient",
                new RawResource(resource.ToString(), FhirResourceFormat.Json, isMetaSet: false),
                null,
                lastModified,
                deleted: deleted,
                searchIndices ?? Array.Empty<SearchIndexEntry>(),
                compartmentIndices: null,
                lastModifiedClaims: null);
        }

        [Fact]
        public async Task MergeAsync_DeletedWrapper_PersistsLatestTombstoneWithLastModified()
        {
            await _fixture.ResetAsync();
            var dataStore = CreateDataStore();

            await dataStore.MergeAsync(new[] { CreateOperation("patient-1") }, CancellationToken.None);
            await dataStore.MergeAsync(new[] { CreateOperation("patient-1", deleted: true) }, CancellationToken.None);

            var documents = await _fixture.GetResourceDocumentsAsync("Patient", "patient-1");
            var latest = Assert.Single(documents, document => document[FieldNameConstants.IsLatest].AsBoolean);

            Assert.Equal("2", latest[FieldNameConstants.Version].AsString);
            Assert.True(latest[FieldNameConstants.IsDeleted].AsBoolean);
            Assert.Equal(new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc), latest[FieldNameConstants.LastModified].ToUniversalTime());

            var read = await dataStore.GetAsync(new ResourceKey("Patient", "patient-1"), CancellationToken.None);
            Assert.True(read!.IsDeleted);
            Assert.Equal(new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero), read.LastModified);
        }

        [Fact]
        public async Task MergeAsync_WhenBuilderFails_LeavesSeededLatestIntactAndThrows()
        {
            await _fixture.ResetAsync();
            await _fixture.SeedAsync("Patient", "patient-1", "1", isLatest: true);
            var dataStore = CreateDataStore(new AlwaysFailingStorageDocumentBuilder());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dataStore.MergeAsync(new[] { CreateOperation("patient-1") }, CancellationToken.None));

            var documents = await _fixture.GetResourceDocumentsAsync("Patient", "patient-1");
            var only = Assert.Single(documents);
            Assert.Equal("1", only[FieldNameConstants.Version].AsString);
            Assert.True(only[FieldNameConstants.IsLatest].AsBoolean);
        }

        private static SearchIndexEntry CreateSearchIndex(string code, string value)
        {
            return new SearchIndexEntry(
                new SearchParameterInfo(code, code, SearchParamType.String),
                new StringSearchValue(value));
        }

        private sealed class MarkedStorageDocumentBuilder : IStorageDocumentBuilder
        {
            private readonly StorageDocumentBuilder _inner = new StorageDocumentBuilder();

            public int CallCount { get; private set; }

            public BsonDocument BuildStorageDocument(
                ResourceWrapper resource,
                string versionId,
                ResourceWrapperOperation resourceExt,
                JObject resourceDocument)
            {
                CallCount++;
                var document = _inner.BuildStorageDocument(resource, versionId, resourceExt, resourceDocument);
                document.Add("builderMarker", true);
                return document;
            }
        }

        private sealed class AlwaysFailingStorageDocumentBuilder : IStorageDocumentBuilder
        {
            public BsonDocument BuildStorageDocument(
                ResourceWrapper resource,
                string versionId,
                ResourceWrapperOperation resourceExt,
                JObject resourceDocument)
            {
                throw new InvalidOperationException("Injected storage-document failure.");
            }
        }

        private sealed class FailingOnSecondStorageDocumentBuilder : IStorageDocumentBuilder
        {
            private readonly StorageDocumentBuilder _inner = new StorageDocumentBuilder();
            private int _callCount;

            public BsonDocument BuildStorageDocument(
                ResourceWrapper resource,
                string versionId,
                ResourceWrapperOperation resourceExt,
                JObject resourceDocument)
            {
                if (Interlocked.Increment(ref _callCount) == 2)
                {
                    throw new InvalidOperationException("Injected storage-document failure.");
                }

                return _inner.BuildStorageDocument(resource, versionId, resourceExt, resourceDocument);
            }
        }
    }
}
