// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Core.Registration
{
    public class MongoDbRuntimeConfiguration : IFhirRuntimeConfiguration
    {
        public string DataStore => KnownDataStores.MongoDb;

        public bool IsSelectiveSearchParameterSupported => false;

#pragma warning disable CA1822 // Mark members as static
        public bool IsExportBackgroundWorkerSupported => true;
#pragma warning restore CA1822 // Mark members as static

        public bool IsCustomerKeyValidationBackgroundWorkerSupported => false;

        public bool IsTransactionSupported => false;

        public bool IsLatencyOverEfficiencySupported => true;

        public bool IsQueryCacheSupported => false;

        public bool IsSurrogateIdRangingSupported => false;
    }
}
