// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.MongoDb.Features.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Microsoft.Health.Fhir.MongoDb.Features.Search.Queries
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Pending")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1852:Seal internal types", Justification = "Pending")]
    internal class QueryBuilder : IQueryBuilder
    {
        public MongoQuerySpec BuildQuerySpec(SearchOptions searchOptions)
        {
            EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

            var expressionQueryBuilder = new ExpressionQueryBuilder();

            ExpressionQueryBuilderContext ctx = new ExpressionQueryBuilderContext();

            searchOptions.Expression?.AcceptVisitor(expressionQueryBuilder, ctx);

            var filters = ctx.GetFilters(searchOptions);

            SortDefinition<BsonDocument> sortDefinition = null;

            if (searchOptions.Sort?.Any() == true)
            {
                // Only the first sort is emitted for the current MongoDB implementation.
                var sort = searchOptions.Sort[0];
                var sortFieldName = sort.searchParameterInfo?.Name;

                if (string.IsNullOrEmpty(sortFieldName))
                {
                    return new MongoQuerySpec(filters, null);
                }

                sortDefinition = sort.sortOrder == SortOrder.Ascending
                    ? Builders<BsonDocument>.Sort.Ascending(sortFieldName)
                    : Builders<BsonDocument>.Sort.Descending(sortFieldName);
            }

            return new MongoQuerySpec(filters, sortDefinition);
        }
    }
}
