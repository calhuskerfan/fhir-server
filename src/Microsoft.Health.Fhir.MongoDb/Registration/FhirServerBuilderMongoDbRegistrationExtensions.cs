// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.MongoDb.Configs;
using Microsoft.Health.Fhir.MongoDb.Features.Health;
using Microsoft.Health.Fhir.MongoDb.Features.Operations.Export;
using Microsoft.Health.Fhir.MongoDb.Features.Search;
using Microsoft.Health.Fhir.MongoDb.Features.Search.Queries;
using Microsoft.Health.Fhir.MongoDb.Features.Storage;
using Microsoft.Health.Fhir.MongoDb.Features.Storage.Queues;
using Microsoft.Health.JobManagement;
using QueryBuilder = Microsoft.Health.Fhir.MongoDb.Features.Search.Queries.QueryBuilder;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// FhirServerBuilderMongoDbRegistrationExtensions is the entrypoint for registering all services
    /// and configurations required to run the Mongo Storage.
    /// </summary>
    public static class FhirServerBuilderMongoDbRegistrationExtensions
    {
        public static IFhirServerBuilder AddMongoDb(
            this IFhirServerBuilder fhirServerBuilder,
            Action<MongoDataStoreConfiguration> configureAction = null)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            IServiceCollection services = fhirServerBuilder.Services;

            services.Add<CompartmentSearchRewriter>()
                .Singleton()
                .AsSelf();

            services.Add<SmartCompartmentSearchRewriter>()
                            .Singleton()
                            .AsSelf();

            return fhirServerBuilder
                .AddMongoDbPersistance(configureAction)
                .AddMongoDbSearch()
                .AddMongoDbHealthCheck();
        }

        private static IFhirServerBuilder AddMongoDbPersistance(
            this IFhirServerBuilder fhirServerBuilder,
            Action<MongoDataStoreConfiguration> configureAction = null)
        {
            IServiceCollection services = fhirServerBuilder.Services;

            services.Add(provider =>
            {
                var config = new MongoDataStoreConfiguration();
                provider.GetService<IConfiguration>().GetSection("MongoDb").Bind(config);
                configureAction?.Invoke(config);
                return config;
            })
            .Singleton()
            .AsSelf();

            services.Add<MongoFhirDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<MongoDbTransactionHandler>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<MongoQueueClient>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces()
                .AsFactory<IScoped<IQueueClient>>();

            IEnumerable<TypeRegistrationBuilder> jobs = services.TypesInSameAssemblyAs<MongoExportOrchestratorJob>()
                .AssignableTo<IJob>()
                .Transient()
                .AsSelf();

            foreach (TypeRegistrationBuilder job in jobs)
            {
                job.AsDelegate<Func<IJob>>();
            }

            // BUGCJH:  Investigate.
            // leave at the bottom
            // services
            //    .RemoveServiceTypeExact<LegacyExportJobWorker, INotificationHandler<StorageInitializedNotification>>()
            //    .Add<LegacyExportJobWorker>()
            //    .Singleton()
            //    .AsSelf()
            //    .AsService<INotificationHandler<StorageInitializedNotification>>();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddMongoDbSearch(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services.Add<FhirMongoSearchService>()
                .Scoped()
                .AsSelf()
            .AsImplementedInterfaces();

            fhirServerBuilder.Services.AddSingleton<IQueryBuilder, QueryBuilder>();

            fhirServerBuilder.Services.Add<MongoDbSortingValidator>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            fhirServerBuilder.Services.Add<MongoDbSearchParameterValidator>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddMongoDbHealthCheck(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services
                .AddHealthChecks()
                .AddCheck<MongoHealthCheck>("MongoCheck");

            return fhirServerBuilder;
        }
    }
}
