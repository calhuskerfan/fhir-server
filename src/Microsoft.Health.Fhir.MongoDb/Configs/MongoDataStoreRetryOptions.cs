// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.MongoDb.Configs
{
    public class MongoDataStoreRetryOptions
    {
        /// <summary>
        /// Gets the maximum number of retries allowed.
        /// </summary>
        public int MaxNumberOfRetries { get; set; }

        /// <summary>
        /// Gets the maximum number of seconds to wait while the retries are happening.
        /// </summary>
        public int MaxWaitTimeInSeconds { get; set; }
    }
}
