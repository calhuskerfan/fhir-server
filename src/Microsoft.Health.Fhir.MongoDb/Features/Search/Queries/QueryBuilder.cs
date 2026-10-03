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
                // BUGCJH: Start With One sort, but we should support multiple sorts in the future
                var sort = searchOptions.Sort[0];

                if (sort.searchParameterInfo.Name == SearchParameterNames.LastUpdated)
                {
                    sortDefinition = sort.sortOrder == SortOrder.Ascending
                    ? Builders<BsonDocument>.Sort.Ascending($"{FieldNameConstants.LastModified}")
                    : Builders<BsonDocument>.Sort.Descending($"{FieldNameConstants.LastModified}");
                }

                // so this is where it starts to suck, becuase we either go to aggregation, or we need to start mapping.  for now I am going
                // to go one by one and do the sorting on the resource itself and not the searchparamaters
                // lets just make a little progress and then come back
                // we could actually 'chop' the Expression to get a little bit of a head start
                if (sort.searchParameterInfo.Name == "family")
                {
                    sortDefinition = sort.sortOrder == SortOrder.Ascending
                    ? Builders<BsonDocument>.Sort.Ascending($"resource.name.{sort.searchParameterInfo.Name}")
                    : Builders<BsonDocument>.Sort.Descending($"resource.name.{sort.searchParameterInfo.Name}");
                }

                if (sort.searchParameterInfo.Name == "birthdate")
                {
                    sortDefinition = sort.sortOrder == SortOrder.Ascending
                    ? Builders<BsonDocument>.Sort.Ascending($"resource.birthdate.{sort.searchParameterInfo.Name}")
                    : Builders<BsonDocument>.Sort.Descending($"resource.birthdate.{sort.searchParameterInfo.Name}");
                }

                /*
                if (sort.searchParameterInfo.Name == "birthdate")
                {
                    sortDefinition = sort.sortOrder == SortOrder.Ascending
                    ? Builders<BsonDocument>.Sort.Ascending($"resource.birthdate.{sort.searchParameterInfo.Name}")
                    : Builders<BsonDocument>.Sort.Descending($"resource.birthdate.{sort.searchParameterInfo.Name}");
                }
                */
            }

            return new MongoQuerySpec(filters, sortDefinition);
        }
    }
}
