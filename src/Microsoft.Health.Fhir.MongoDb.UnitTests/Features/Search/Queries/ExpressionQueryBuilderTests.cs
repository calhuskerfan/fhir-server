// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.MongoDb.Features.Search.Queries;
using Microsoft.Health.Fhir.MongoDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using MongoDB.Bson;
using Xunit;

namespace Microsoft.Health.Fhir.MongoDb.UnitTests.Features.Search.Queries
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ExpressionQueryBuilderTests
    {
        [Fact]
        public void VisitString_WithEndsWithOperator_AddsRegexCondition()
        {
            var builder = new ExpressionQueryBuilder();
            var context = new ExpressionQueryBuilderContext();
            var expression = new StringExpression(StringOperator.EndsWith, FieldName.String, null, "test", ignoreCase: false);

            context.Assembler.StartNewFilter();
            builder.VisitString(expression, context);
            context.Assembler.CompleteFilter();

            var filter = context.GetFilters(new SearchOptions());
            var condition = filter["$and"][0]["searchIndexes"]["$elemMatch"]["Value.String"];

            Assert.IsType<BsonRegularExpression>(condition);
            Assert.Equal(".*test$", condition.AsBsonRegularExpression.Pattern);
        }

        [Fact]
        public void VisitString_WithNotContainsOperator_AddsNegatedRegexCondition()
        {
            var builder = new ExpressionQueryBuilder();
            var context = new ExpressionQueryBuilderContext();
            var expression = new StringExpression(StringOperator.NotContains, FieldName.String, null, "test", ignoreCase: false);

            context.Assembler.StartNewFilter();
            builder.VisitString(expression, context);
            context.Assembler.CompleteFilter();

            var filter = context.GetFilters(new SearchOptions());
            var condition = filter["$and"][0]["searchIndexes"]["$elemMatch"]["Value.String"];

            Assert.IsType<BsonDocument>(condition);
            Assert.True(condition.AsBsonDocument.Contains("$not"));
            Assert.Equal(".*test.*", condition["$not"].AsBsonRegularExpression.Pattern);
        }

        [Fact]
        public void VisitString_WithNotStartsWithOperator_AddsNegatedRegexCondition()
        {
            var builder = new ExpressionQueryBuilder();
            var context = new ExpressionQueryBuilderContext();
            var expression = new StringExpression(StringOperator.NotStartsWith, FieldName.String, null, "test", ignoreCase: false);

            context.Assembler.StartNewFilter();
            builder.VisitString(expression, context);
            context.Assembler.CompleteFilter();

            var filter = context.GetFilters(new SearchOptions());
            var condition = filter["$and"][0]["searchIndexes"]["$elemMatch"]["Value.String"];

            Assert.IsType<BsonDocument>(condition);
            Assert.True(condition.AsBsonDocument.Contains("$not"));
            Assert.Equal("^test.*", condition["$not"].AsBsonRegularExpression.Pattern);
        }

        [Fact]
        public void VisitString_WithNotEndsWithOperator_AddsNegatedRegexCondition()
        {
            var builder = new ExpressionQueryBuilder();
            var context = new ExpressionQueryBuilderContext();
            var expression = new StringExpression(StringOperator.NotEndsWith, FieldName.String, null, "test", ignoreCase: false);

            context.Assembler.StartNewFilter();
            builder.VisitString(expression, context);
            context.Assembler.CompleteFilter();

            var filter = context.GetFilters(new SearchOptions());
            var condition = filter["$and"][0]["searchIndexes"]["$elemMatch"]["Value.String"];

            Assert.IsType<BsonDocument>(condition);
            Assert.True(condition.AsBsonDocument.Contains("$not"));
            Assert.Equal(".*test$", condition["$not"].AsBsonRegularExpression.Pattern);
        }

        [Fact]
        public void VisitString_WithLeftSideStartsWithOperator_AddsPrefixRegexCondition()
        {
            var builder = new ExpressionQueryBuilder();
            var context = new ExpressionQueryBuilderContext();
            var expression = new StringExpression(StringOperator.LeftSideStartsWith, FieldName.String, null, "test", ignoreCase: false);

            context.Assembler.StartNewFilter();
            builder.VisitString(expression, context);
            context.Assembler.CompleteFilter();

            var filter = context.GetFilters(new SearchOptions());
            var condition = filter["$and"][0]["searchIndexes"]["$elemMatch"]["Value.String"];

            Assert.IsType<BsonRegularExpression>(condition);
            Assert.Equal("^test.*", condition.AsBsonRegularExpression.Pattern);
        }

        [Fact]
        public void VisitSearchParameter_WithLastUpdated_UsesResourceLastModifiedField()
        {
            var builder = new ExpressionQueryBuilder();
            var context = new ExpressionQueryBuilderContext();
            var lastUpdated = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var expression = new SearchParameterExpression(
                new SearchParameterInfo(SearchParameterNames.LastUpdated, SearchParameterNames.LastUpdated),
                new BinaryExpression(BinaryOperator.GreaterThanOrEqual, FieldName.DateTimeStart, null, lastUpdated));

            builder.VisitSearchParameter(expression, context);

            var filter = context.GetFilters(new SearchOptions());
            var condition = filter["$and"][0][FieldNameConstants.LastModified]["$gte"];

            Assert.Equal(new BsonDateTime(lastUpdated.DateTime), condition);
        }
    }
}
