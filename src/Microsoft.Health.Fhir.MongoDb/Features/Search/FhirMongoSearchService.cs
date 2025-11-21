// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.MongoDb.Configs;
using Microsoft.Health.Fhir.MongoDb.Features.Search.Queries;
using Microsoft.Health.Fhir.MongoDb.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Search;

namespace Microsoft.Health.Fhir.MongoDb.Features.Search
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Pending")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1852:Seal internal types", Justification = "Pending")]
    internal class FhirMongoSearchService : SearchService
    {
        private readonly MongoFhirDataStore _fhirDataStore;
        private readonly IQueryBuilder _queryBuilder;
        private readonly MongoDataStoreConfiguration _dataStoreConfiguration;
        private readonly ILogger<FhirMongoSearchService> _logger;

        public FhirMongoSearchService(
            ISearchOptionsFactory searchOptionsFactory,
            MongoFhirDataStore fhirDataStore,
            IQueryBuilder queryBuilder,
            CompartmentSearchRewriter compartmentSearchRewriter,
            SmartCompartmentSearchRewriter smartCompartmentSearchRewriter,
            ILogger<FhirMongoSearchService> logger,
            MongoDataStoreConfiguration dataStoreConfiguration)
            : base(searchOptionsFactory, fhirDataStore, logger)
        {
            EnsureArg.IsNotNull(searchOptionsFactory, nameof(searchOptionsFactory));
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(queryBuilder, nameof(queryBuilder));
            EnsureArg.IsNotNull(compartmentSearchRewriter, nameof(compartmentSearchRewriter));
            EnsureArg.IsNotNull(smartCompartmentSearchRewriter, nameof(smartCompartmentSearchRewriter));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirDataStore = fhirDataStore;
            _queryBuilder = queryBuilder;
            _logger = logger;
            _dataStoreConfiguration = dataStoreConfiguration;
        }

        public override Task<IReadOnlyList<string>> GetUsedResourceTypes(CancellationToken cancellationToken)
        {
            _logger.LogInformation("GetUsedResourceTypes");
            throw new NotImplementedException();
        }

        // SearchImpl entrypoint
        private async Task<SearchResult> SearchImpl(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
#if SEARCH_INCLUDE_FUNCTIONALITY
            // TODOCJH:  This is using the Cosmos Implementation as a guide.
            // validate !
            // we're going to mutate searchOptions, so clone it first so the caller of this method does not see the changes.
            searchOptions = searchOptions.Clone();

            // Starting with the Cosmos approach first to see where it takes us.
            bool hasIncludeOrRevIncludeExpressions = searchOptions.Expression.ExtractIncludeAndChainedExpressions(
                out Expression expressionWithoutIncludes,
                out IReadOnlyList<IncludeExpression> includeExpressions,
                out IReadOnlyList<IncludeExpression> revIncludeExpressions,
                out IReadOnlyList<ChainedExpression> chainedExpressions);

            if (hasIncludeOrRevIncludeExpressions)
            {
                searchOptions.Expression = expressionWithoutIncludes;

                if (includeExpressions.Any(e => e.Iterate) ||
                    revIncludeExpressions.Any(e => e.Iterate))
                {
                    _logger.LogWarning("Bad Request (IncludeIterateNotSupported)");
                    throw new BadRequestException(Resources.IncludeIterateNotSupported);
                }
            }

            if (hasIncludeOrRevIncludeExpressions && chainedExpressions.Count > 0)
            {
                _logger.LogWarning("Bad Request (ChainedExpressions)");
                throw new BadRequestException("Chained Expressions Not Supported");
            }
#endif
            var filter = _queryBuilder.BuildFilterSpec(searchOptions);

            _logger.LogDebug(filter.ToString());

            // TODOCJH:  Is this a candidate for yield ? revisit when we have finished includes

            var documents = await _dataStoreConfiguration
                .GetCollection()
                .Find(filter)
                .Limit(searchOptions.MaxItemCount)
                .ToListAsync(cancellationToken);

            List<FHIRMongoResourceWrapper> resourceWrappers = [];

            foreach (var entry in documents)
            {
                var resourceWrapper = FHIRMongoResourceWrapper.FromBsonDocument(entry);
                resourceWrappers.Add(resourceWrapper);
            }

#if SEARCH_INCLUDE_FUNCTIONALITY
            (IList<ResourceWrapper> includes, bool includesTruncated) = await PerformIncludeQueriesAsync(temporaryResourceWrappers, includeExpressions, revIncludeExpressions, searchOptions.IncludeCount, cancellationToken);
#endif
            SearchResult searchResult = CreateSearchResult(
                searchOptions,
                resourceWrappers.Select(m => new SearchResultEntry(m)),
                null,
                false);

            return searchResult;
        }

#pragma warning disable CA1822
        private SearchResult CreateSearchResult(
            SearchOptions searchOptions,
            IEnumerable<SearchResultEntry> results,
            string? continuationToken,
            bool includesTruncated = false)
        {
            /*
            if (includesTruncated)
                {
                    _requestContextAccessor.RequestContext.BundleIssues.Add(
                        new OperationOutcomeIssue(
                            OperationOutcomeConstants.IssueSeverity.Warning,
                            OperationOutcomeConstants.IssueType.Incomplete,
                            Microsoft.Health.Fhir.Core.Resources.TruncatedIncludeMessage));
                }
            */

            return new SearchResult(
                results,
                continuationToken,
                searchOptions.Sort,
                searchOptions.UnsupportedSearchParams);
        }
#pragma warning restore CA1822

        public override async Task<SearchResult> SearchAsync(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            _logger.LogInformation("SearchAsync");

            SearchResult searchResult = await SearchImpl(searchOptions, cancellationToken);

            return searchResult;
        }

        protected override Task<SearchResult> SearchForReindexInternalAsync(SearchOptions searchOptions, string searchParameterHash, CancellationToken cancellationToken)
        {
            _logger.LogInformation("SearchForReindexInternalAsync");
            throw new NotImplementedException();
        }

#if SEARCH_INCLUDE_FUNCTIONALITY
#pragma warning disable CA1822
#pragma warning disable CS1998
        private async Task<(IList<ResourceWrapper> includes, bool includesTruncated)> PerformIncludeQueriesAsync(
            List<ResourceWrapper> matches,
            IReadOnlyCollection<IncludeExpression> includeExpressions,
            IReadOnlyCollection<IncludeExpression> revIncludeExpressions,
            int maxIncludeCount,
            CancellationToken cancellationToken)
        {
            // if no matches or no include/revinclude then just return empty
            if (matches.Count == 0 ||
                (includeExpressions.Count == 0 && revIncludeExpressions.Count == 0))
            {
                return (Array.Empty<ResourceWrapper>(), false);
            }

            var includes = new List<ResourceWrapper>();

            var matchIds = matches
                .Select(x => new ResourceKey(x.ResourceTypeName, x.ResourceId))
                .ToHashSet();

            if (includeExpressions.Count > 0)
            {
                // fetch in the resources to include from _include parameters.

                // var referencesToInclude = matches
                //    .SelectMany(m => m.ReferencesToInclude)
                //    .Where(r => r.ResourceTypeName != null) // exclude untyped references to align with the current SQL behavior
                //    .Select(x => new ResourceKey(x.ResourceTypeName, x.ResourceId))
                //    .Distinct()
                //    .Where(x => !matchIds.Contains(x))
                //    .ToList();
            }

            throw new NotImplementedException();
        }
#endif
#pragma warning restore CS1998
#pragma warning restore CA1822
    }
}
