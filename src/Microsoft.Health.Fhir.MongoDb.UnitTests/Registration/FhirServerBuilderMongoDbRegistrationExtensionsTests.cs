// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.MongoDb.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirServerBuilderMongoDbRegistrationExtensionsTests
    {
        [Fact]
        public void GivenMongoDbRegistration_WhenResolvingDeletionServiceDataStoreFactory_ThenFactoryIsRegistered()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());
            services.AddLogging();
            services.AddSingleton<IOptions<CoreFeatureConfiguration>>(Options.Create(new CoreFeatureConfiguration()));
            services.AddSingleton<RequestContextAccessor<IFhirRequestContext>, FhirRequestContextAccessor>();
            services.AddSingleton<IBundleOrchestrator>(Substitute.For<IBundleOrchestrator>());
            services.AddSingleton<IFhirDataStore>(Substitute.For<IFhirDataStore>());

            IFhirServerBuilder builder = new TestFhirServerBuilder(services);
            builder.AddMongoDb();

            using ServiceProvider provider = services.BuildServiceProvider();
            IDeletionServiceDataStoreFactory factory = provider.GetRequiredService<IDeletionServiceDataStoreFactory>();

            Assert.NotNull(factory);
        }

        private sealed class TestFhirServerBuilder : IFhirServerBuilder
        {
            public TestFhirServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
