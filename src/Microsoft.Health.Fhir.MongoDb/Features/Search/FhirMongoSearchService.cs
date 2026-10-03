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
using Newtonsoft.Json.Linq;

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
            searchOptions = searchOptions.Clone();

            Expression expressionWithoutIncludes = searchOptions.Expression;
            IReadOnlyList<IncludeExpression> includeExpressions = Array.Empty<IncludeExpression>();
            IReadOnlyList<IncludeExpression> revIncludeExpressions = Array.Empty<IncludeExpression>();
            IReadOnlyList<ChainedExpression> chainedExpressions = Array.Empty<ChainedExpression>();

            bool hasIncludeOrRevIncludeExpressions = searchOptions.Expression?.ExtractIncludeAndChainedExpressions(
                out expressionWithoutIncludes,
                out includeExpressions,
                out revIncludeExpressions,
                out chainedExpressions,
                out IReadOnlyList<UnionExpression> _) ?? false;

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

            var querySpec = _queryBuilder.BuildQuerySpec(searchOptions);

            _logger.LogDebug(querySpec.Filter.ToString());

            var find = _dataStoreConfiguration
                .GetCollection()
                .Find(querySpec.Filter);

            if (querySpec.Sort != null)
            {
                find = find.Sort(querySpec.Sort);
            }

            var documents = await find
                .Limit(searchOptions.MaxItemCount)
                .ToListAsync(cancellationToken);

            List<FHIRMongoResourceWrapper> resourceWrappers = [];

            foreach (var entry in documents)
            {
                var resourceWrapper = FHIRMongoResourceWrapper.FromBsonDocument(entry);
                resourceWrappers.Add(resourceWrapper);
            }

            (IList<ResourceWrapper> includes, bool includesTruncated) = await PerformIncludeQueriesAsync(
                resourceWrappers,
                includeExpressions,
                revIncludeExpressions,
                searchOptions.IncludeCount,
                cancellationToken);

            SearchResult searchResult = CreateSearchResult(
                searchOptions,
                resourceWrappers.Select(m => new SearchResultEntry(m, SearchEntryMode.Match)).Concat(includes.Select(i => new SearchResultEntry(i, SearchEntryMode.Include))),
                null,
                includesTruncated);

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

#pragma warning disable CA1822
#pragma warning disable CS1998
        private async Task<(IList<ResourceWrapper> includes, bool includesTruncated)> PerformIncludeQueriesAsync(
            List<FHIRMongoResourceWrapper> matches,
            IReadOnlyCollection<IncludeExpression> includeExpressions,
            IReadOnlyCollection<IncludeExpression> revIncludeExpressions,
            int maxIncludeCount,
            CancellationToken cancellationToken)
        {
            if (matches.Count == 0 ||
                (includeExpressions.Count == 0 && revIncludeExpressions.Count == 0))
            {
                return (Array.Empty<ResourceWrapper>(), false);
            }

            var includes = new List<ResourceWrapper>();
            var seenIncludeKeys = new HashSet<ResourceKey>();

            if (includeExpressions.Count > 0)
            {
                foreach (IncludeExpression includeExpression in includeExpressions)
                {
                    string code = includeExpression.ReferenceSearchParameter?.Code;
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    var targetTypes = includeExpression.TargetResourceType != null
                        ? new[] { includeExpression.TargetResourceType }
                        : includeExpression.ReferenceSearchParameter?.TargetResourceTypes?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>();

                    foreach (FHIRMongoResourceWrapper match in matches)
                    {
                        if (!string.Equals(match.ResourceTypeName, includeExpression.SourceResourceType, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(includeExpression.SourceResourceType, "*", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        foreach ((string resourceType, string resourceId) in ExtractReferenceTargets(match, code))
                        {
                            if (targetTypes.Length > 0 &&
                                !targetTypes.Contains(resourceType, StringComparer.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var resourceKey = new ResourceKey(resourceType, resourceId);
                            if (seenIncludeKeys.Contains(resourceKey))
                            {
                                continue;
                            }

                            ResourceWrapper resource = await _fhirDataStore.GetAsync(resourceKey, cancellationToken);
                            if (resource == null || resource.IsDeleted)
                            {
                                continue;
                            }

                            includes.Add(resource);
                            seenIncludeKeys.Add(resourceKey);

                            if (includes.Count > maxIncludeCount)
                            {
                                return (includes, true);
                            }
                        }
                    }
                }
            }

            if (revIncludeExpressions.Count > 0)
            {
                foreach (IncludeExpression revIncludeExpression in revIncludeExpressions)
                {
                    string code = revIncludeExpression.ReferenceSearchParameter?.Code;
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    var targetResourceTypes = revIncludeExpression.TargetResourceType != null
                        ? new[] { revIncludeExpression.TargetResourceType }
                        : revIncludeExpression.ReferenceSearchParameter?.TargetResourceTypes?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>();

                    if (targetResourceTypes.Length == 0)
                    {
                        continue;
                    }

                    var matchResourceTypes = matches
                        .Select(m => m.ResourceTypeName)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var matchResourceIds = matches
                        .Select(m => m.ResourceId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (string targetResourceType in targetResourceTypes)
                    {
                        var filter = Builders<BsonDocument>.Filter.And(
                            Builders<BsonDocument>.Filter.Eq(FieldNameConstants.ResourceType, targetResourceType),
                            Builders<BsonDocument>.Filter.ElemMatch(
                                FieldNameConstants.SearchIndexes,
                                Builders<BsonDocument>.Filter.And(
                                    Builders<BsonDocument>.Filter.Eq($"{FieldNameConstants.SearchParameter}.{FieldNameConstants.SearchParameterCode}", code),
                                    Builders<BsonDocument>.Filter.In($"Value.{SearchValueConstants.ReferenceResourceTypeName}", matchResourceTypes),
                                    Builders<BsonDocument>.Filter.In($"Value.{SearchValueConstants.ReferenceResourceIdName}", matchResourceIds))));

                        var docs = await _dataStoreConfiguration
                            .GetCollection()
                            .Find(filter)
                            .Limit(maxIncludeCount - includes.Count)
                            .ToListAsync(cancellationToken);

                        foreach (var doc in docs)
                        {
                            var resource = FHIRMongoResourceWrapper.FromBsonDocument(doc);
                            if (resource == null || resource.IsDeleted)
                            {
                                continue;
                            }

                            var resourceKey = new ResourceKey(resource.ResourceTypeName, resource.ResourceId);
                            if (seenIncludeKeys.Contains(resourceKey))
                            {
                                continue;
                            }

                            includes.Add(resource);
                            seenIncludeKeys.Add(resourceKey);

                            if (includes.Count > maxIncludeCount)
                            {
                                return (includes, true);
                            }
                        }
                    }
                }
            }

            return (includes, false);
        }

        private static IEnumerable<(string ResourceType, string ResourceId)> ExtractReferenceTargets(FHIRMongoResourceWrapper match, string searchParameterCode)
        {
            if (match?.RawResource == null || string.IsNullOrWhiteSpace(match.RawResource.Data) || string.IsNullOrWhiteSpace(searchParameterCode))
            {
                return Array.Empty<(string ResourceType, string ResourceId)>();
            }

            try
            {
                JObject resourceJson = JObject.Parse(match.RawResource.Data);
                JToken token = resourceJson[searchParameterCode];
                if (token == null)
                {
                    return Array.Empty<(string ResourceType, string ResourceId)>();
                }

                List<(string ResourceType, string ResourceId)> results = new();
                foreach (string reference in EnumerateReferenceValues(token))
                {
                    if (TryParseReference(reference, out string resourceType, out string resourceId))
                    {
                        results.Add((resourceType, resourceId));
                    }
                }

                return results;
            }
            catch (Exception)
            {
                return Array.Empty<(string ResourceType, string ResourceId)>();
            }
        }

        private static IEnumerable<string> EnumerateReferenceValues(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Array:
                    var results = new List<string>();
                    foreach (JToken item in token.Children())
                    {
                        results.AddRange(EnumerateReferenceValues(item));
                    }

                    return results;
                case JTokenType.Object:
                    if (token["reference"] != null)
                    {
                        string? reference = token["reference"]?.Value<string>();
                        return reference is null ? Array.Empty<string>() : new[] { reference };
                    }

                    return Array.Empty<string>();
                case JTokenType.String:
                    string? text = token.Value<string>();
                    return text is null ? Array.Empty<string>() : new[] { text };
                default:
                    return Array.Empty<string>();
            }
        }

        private static bool TryParseReference(string reference, out string resourceType, out string resourceId)
        {
            resourceType = null;
            resourceId = null;

            if (string.IsNullOrWhiteSpace(reference))
            {
                return false;
            }

            string candidate = reference.Trim();
            if (candidate.Contains('/', StringComparison.Ordinal))
            {
                string[] parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2)
                {
                    resourceType = parts[^2];
                    resourceId = parts[^1];
                    return !string.IsNullOrWhiteSpace(resourceType) && !string.IsNullOrWhiteSpace(resourceId);
                }
            }

            resourceId = candidate;
            return !string.IsNullOrWhiteSpace(resourceId);
        }
#pragma warning restore CS1998
#pragma warning restore CA1822
    }
}
