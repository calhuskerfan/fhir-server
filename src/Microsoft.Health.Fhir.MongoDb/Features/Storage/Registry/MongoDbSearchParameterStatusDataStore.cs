// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.MongoDb.Features.Storage.Registry
{
    internal sealed class MongoDbSearchParameterStatusDataStore : ISearchParameterStatusDataStore
    {
        private static readonly HashSet<Uri> InitialSortParameterUris = new HashSet<Uri>
        {
            SearchParameterNames.LastUpdatedUri,
            new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
            new Uri("http://hl7.org/fhir/SearchParameter/individual-family"),
            /*
            new Uri("http://hl7.org/fhir/SearchParameter/individual-given"),
            */
        };

        private readonly FilebasedSearchParameterStatusDataStore _filebasedDataStore;

        public MongoDbSearchParameterStatusDataStore(FilebasedSearchParameterStatusDataStore filebasedDataStore)
        {
            EnsureArg.IsNotNull(filebasedDataStore, nameof(filebasedDataStore));
            _filebasedDataStore = filebasedDataStore;
        }

        public string SearchParamCacheUpdateProcessName => _filebasedDataStore.SearchParamCacheUpdateProcessName;

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken, DateTimeOffset? startLastUpdated = null)
        {
            IReadOnlyCollection<ResourceSearchParameterStatus> statuses = await _filebasedDataStore.GetSearchParameterStatuses(cancellationToken, startLastUpdated);

            return statuses.Select(status => new ResourceSearchParameterStatus
            {
                Uri = status.Uri,
                Status = status.Status,
                IsPartiallySupported = status.IsPartiallySupported,
                SortStatus = status.SortStatus != SortParameterStatus.Disabled
                    ? status.SortStatus
                    : InitialSortParameterUris.Contains(status.Uri) ? SortParameterStatus.Enabled : SortParameterStatus.Disabled,
                LastUpdated = status.LastUpdated,
            }).ToArray();
        }

        public Task UpsertStatuses(IReadOnlyList<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken, long? reindexId = null)
            => _filebasedDataStore.UpsertStatuses(statuses, cancellationToken, reindexId);

        public void SyncStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses)
            => _filebasedDataStore.SyncStatuses(statuses);

        public Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
            => _filebasedDataStore.TryLogEvent(process, status, text, startDate, cancellationToken);

        public Task<CacheConsistencyResult> CheckCacheConsistencyAsync(DateTime updateEventsSince, DateTime activeHostsSince, CancellationToken cancellationToken)
            => _filebasedDataStore.CheckCacheConsistencyAsync(updateEventsSince, activeHostsSince, cancellationToken);
    }
}
