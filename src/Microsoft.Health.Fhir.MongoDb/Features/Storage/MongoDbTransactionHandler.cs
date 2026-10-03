// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Fhir.MongoDb.Configs;

namespace Microsoft.Health.Fhir.MongoDb.Features.Storage
{
    internal sealed class MongoDbTransactionHandler : ITransactionHandler
    {
        private readonly MongoDataStoreConfiguration _configuration;

        public MongoDbTransactionHandler(MongoDataStoreConfiguration configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            _configuration = configuration;
        }

        public ITransactionScope BeginTransaction()
        {
            return new MongoDbTransactionScope(_configuration.GetClient().StartSession());
        }

        public void Dispose()
        {
        }
    }
}
