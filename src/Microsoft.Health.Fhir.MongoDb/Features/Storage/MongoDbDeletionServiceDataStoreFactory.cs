// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.MongoDb.Features.Storage
{
    internal class MongoDbDeletionServiceDataStoreFactory : IDeletionServiceDataStoreFactory
    {
        private readonly ILogger<MongoDbDeletionServiceDataStoreFactory> _logger;
        private readonly IFhirDataStore _dataStore;

        public MongoDbDeletionServiceDataStoreFactory(
            ILogger<MongoDbDeletionServiceDataStoreFactory> logger,
            IFhirDataStore dataStore)
        {
            _logger = logger;
            _dataStore = EnsureArg.IsNotNull(dataStore, nameof(dataStore));
        }

        Core.Features.Persistence.DeletionServiceScopedDataStore IDeletionServiceDataStoreFactory.GetScopedDataStore()
        {
            return new DeletionServiceScopedDataStore(_dataStore);
        }
    }
}
