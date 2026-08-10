// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.MongoDb.Configs;
using Microsoft.Health.Fhir.MongoDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.MongoDb.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
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
