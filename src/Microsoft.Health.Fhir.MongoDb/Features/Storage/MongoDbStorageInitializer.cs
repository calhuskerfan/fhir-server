// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Messages.Storage;

namespace Microsoft.Health.Fhir.MongoDb.Features.Storage
{
    // MongoDB has no collection provisioning step, but the search parameter definition/status
    // pipeline only starts once StorageInitializedNotification is published (see SqlServerFhirModel
    // and CosmosContainerProvider). Without this, SortStatus is never refreshed from the registry.
    internal sealed class MongoDbStorageInitializer : IHostedService
    {
        private readonly IMediator _mediator;

        public MongoDbStorageInitializer(IMediator mediator)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            _mediator = mediator;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _mediator.PublishAsync(new StorageInitializedNotification(), cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
