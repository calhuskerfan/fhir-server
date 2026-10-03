// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.MongoDb.Features.Search.Queries;
using Microsoft.Health.Fhir.MongoDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Xunit;

namespace Microsoft.Health.Fhir.MongoDb.UnitTests.Features.Search.Queries
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class QueryBuilderSortingTests
    {
        [Fact]
        public void GivenAscendingSort_WhenBuildingSpec_ThenSortsBySearchParameterNameAscending()
        {
            SearchOptions searchOptions = CreateSearchOptions("name", SortOrder.Ascending);

            MongoQuerySpec querySpec = new QueryBuilder().BuildQuerySpec(searchOptions);

            Assert.Equal(new BsonDocument("name", 1), RenderSort(querySpec.Sort));
        }

        [Fact]
        public void GivenDescendingSort_WhenBuildingSpec_ThenSortsBySearchParameterNameDescending()
        {
            SearchOptions searchOptions = CreateSearchOptions("name", SortOrder.Descending);

            MongoQuerySpec querySpec = new QueryBuilder().BuildQuerySpec(searchOptions);

            Assert.Equal(new BsonDocument("name", -1), RenderSort(querySpec.Sort));
        }

        [Fact]
        public void GivenSearchParameterWithDifferentNameAndCode_WhenBuildingSpec_ThenSortsByName()
        {
            var searchOptions = new SearchOptions
            {
                Sort = [(new SearchParameterInfo("family", "fam"), SortOrder.Ascending)],
            };

            MongoQuerySpec querySpec = new QueryBuilder().BuildQuerySpec(searchOptions);

            Assert.Equal(new BsonDocument("family", 1), RenderSort(querySpec.Sort));
        }

        [Fact]
        public void GivenLastUpdatedSort_WhenBuildingSpec_ThenSortsByLastUpdatedCode()
        {
            SearchOptions searchOptions = CreateSearchOptions(KnownQueryParameterNames.LastUpdated, SortOrder.Descending);

            MongoQuerySpec querySpec = new QueryBuilder().BuildQuerySpec(searchOptions);

            Assert.Equal(new BsonDocument(KnownQueryParameterNames.LastUpdated, -1), RenderSort(querySpec.Sort));
        }

        [Fact]
        public void GivenNoSort_WhenBuildingSpec_ThenSortIsNull()
        {
            var searchOptions = new SearchOptions();

            MongoQuerySpec querySpec = new QueryBuilder().BuildQuerySpec(searchOptions);

            Assert.Null(querySpec.Sort);
        }

        [Fact]
        public void GivenMultipleSorts_WhenBuildingSpec_ThenOnlyFirstSortIsEmitted()
        {
            var searchOptions = new SearchOptions
            {
                Sort =
                [
                    (new SearchParameterInfo("name", "name"), SortOrder.Ascending),
                    (new SearchParameterInfo("birthdate", "birthdate"), SortOrder.Descending),
                ],
            };

            MongoQuerySpec querySpec = new QueryBuilder().BuildQuerySpec(searchOptions);

            Assert.Equal(new BsonDocument("name", 1), RenderSort(querySpec.Sort));
        }

        [Fact]
        public void GivenFilterAndSort_WhenBuildingSpec_ThenBothAreIncluded()
        {
            var searchOptions = new SearchOptions
            {
                Expression = new StringExpression(StringOperator.Contains, FieldName.String, null, "test", ignoreCase: false),
                Sort = [(new SearchParameterInfo("name", "name"), SortOrder.Ascending)],
            };

            MongoQuerySpec querySpec = new QueryBuilder().BuildQuerySpec(searchOptions);

            Assert.NotNull(querySpec.Filter);
            Assert.NotEmpty(querySpec.Filter);
            Assert.Equal(new BsonDocument("name", 1), RenderSort(querySpec.Sort));
        }

        private static SearchOptions CreateSearchOptions(string code, SortOrder sortOrder)
        {
            return new SearchOptions
            {
                Sort = [(new SearchParameterInfo(code, code), sortOrder)],
            };
        }

        private static BsonDocument RenderSort(SortDefinition<BsonDocument> sortDefinition)
        {
            return sortDefinition.Render(new RenderArgs<BsonDocument>(
                BsonSerializer.LookupSerializer<BsonDocument>(),
                BsonSerializer.SerializerRegistry));
        }
    }
}
