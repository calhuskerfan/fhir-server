// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.MongoDb
{
    internal static class Constants
    {
        public const string CollectionConfigurationName = "fhirMongoDb";
        public const string MongoDbResponseMessagesProperty = nameof(MongoDbResponseMessagesProperty);
        public const int ContinuationTokenMinLimit = 1;
        public const int ContinuationTokenMaxLimit = 3;
        public const int ContinuationTokenDefaultLimit = 3;
    }
}
