// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.MongoDb.Configs;
using Microsoft.Health.Fhir.MongoDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using MongoDB.Bson;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.MongoDb.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class JObjectExtensionsTests
    {
        [Fact]
        public async Task MergeAsync_WithEmptyResourceList_ReturnsEmptyMergeOutcome()
        {
            var dataStore = CreateDataStore();

            MergeOutcome mergeOutcome = await ((IFhirDataStore)dataStore).MergeAsync(Array.Empty<ResourceWrapperOperation>(), CancellationToken.None);

            Assert.NotNull(mergeOutcome);
            Assert.Equal(MergeOutcomeFinalState.Unchanged, mergeOutcome.State);
            Assert.Empty(mergeOutcome.Results);
        }

        [Fact]
        public void UpdateResourceMetadata_RefreshesMetaVersionAndLastUpdated()
        {
            var resource = JObject.Parse("{\"resourceType\":\"Patient\",\"id\":\"patient-1\",\"meta\":{\"versionId\":\"1\"}}");
            var wrapper = new ResourceWrapper(
                "patient-1",
                "2",
                "Patient",
                new RawResource("{}", FhirResourceFormat.Json, isMetaSet: false),
                null,
                new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null);

            var updated = MongoFhirDataStore.UpdateResourceMetadata(resource, wrapper, "2");

            Assert.Equal("2", updated["meta"]?["versionId"]?.Value<string>());
            Assert.Equal(wrapper.LastModified.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture), updated["meta"]?["lastUpdated"]?.Value<string>());
        }

        [Fact]
        public void FromBsonDocument_UsesVersionedMetadata_WhenPresent()
        {
            var entry = new BsonDocument
            {
                { FieldNameConstants.ResourceId, "patient-1" },
                { FieldNameConstants.ResourceType, "Patient" },
                { FieldNameConstants.Version, "2" },
                { FieldNameConstants.IsDeleted, false },
                {
                    FieldNameConstants.Resource,
                    new BsonDocument
                    {
                        { "id", "patient-1" },
                        { "resourceType", "Patient" },
                        { "name", new BsonArray { new BsonDocument { { "family", "Contoso" } } } },
                    }
                },
            };

            var wrapper = FHIRMongoResourceWrapper.FromBsonDocument(entry);

            Assert.Equal("patient-1", wrapper.ResourceId);
            Assert.Equal("2", wrapper.Version);
            Assert.Equal("Patient", wrapper.ResourceTypeName);
        }

        private static MongoFhirDataStore CreateDataStore()
        {
            var logger = Substitute.For<ILogger<MongoFhirDataStore>>();
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var bundleOrchestrator = Substitute.For<IBundleOrchestrator>();
            var coreFeatures = Options.Create(new CoreFeatureConfiguration());
            var config = new MongoDataStoreConfiguration();

            return new MongoFhirDataStore(logger, requestContextAccessor, bundleOrchestrator, coreFeatures, config);
        }
    }
}
