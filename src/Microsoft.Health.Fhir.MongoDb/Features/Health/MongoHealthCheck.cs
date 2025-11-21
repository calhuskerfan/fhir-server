// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.MongoDb.Features.Health
{
    public class MongoHealthCheck : IHealthCheck
    {
        private readonly ILogger<MongoHealthCheck> _logger;

        public MongoHealthCheck(ILogger<MongoHealthCheck> logger)
        {
            _logger = logger;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("CheckHealthAsync");
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}
