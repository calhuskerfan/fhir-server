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
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Microsoft.Health.Fhir.MongoDb.Features.Search.Queries
{
    internal sealed class QueryBuilderHelper
    {
        public QueryBuilderHelper()
        {
        }

        // returns a BSON document containing the
        // filter specification from the
        // search option expressions
        public MongoQuerySpec BuildFilterSpec(SearchOptions searchOptions)
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

                // $"{FieldNameConstants.SearchParameter}.{FieldNameConstants.SearchParameterCode}", expression.Parameter.Code)

                /*
                sortDefinition = sort.sortOrder == SortOrder.Ascending
                    ? Builders<BsonDocument>.Sort.Ascending($"{FieldNameConstants.SearchParameter}.{FieldNameConstants.SearchParameterCode}.{sort.searchParameterInfo.Code}")
                    : Builders<BsonDocument>.Sort.Descending($"{FieldNameConstants.SearchParameter}.{FieldNameConstants.SearchParameterCode}.{sort.searchParameterInfo.Code}");

                sortDefinition = Builders<BsonDocument>.Sort.Descending(a => a[FieldNameConstants.SearchParameter][FieldNameConstants.SearchParameterCode][sort.searchParameterInfo.Code]);
                */
            }

            return new MongoQuerySpec(filters, sortDefinition);
        }
    }
}
