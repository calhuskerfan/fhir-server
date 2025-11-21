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
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

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
        public BsonDocument BuildFilterSpec(SearchOptions searchOptions)
        {
            EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

            var expressionQueryBuilder = new ExpressionQueryBuilder();

            ExpressionQueryBuilderContext ctx = new ExpressionQueryBuilderContext();

            searchOptions.Expression?.AcceptVisitor(expressionQueryBuilder, ctx);

            return ctx.GetFilters();
        }
    }
}
