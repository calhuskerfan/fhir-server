// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.MongoDb.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.MongoDb.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class MongoDbSortingValidatorTests
    {
        private readonly FhirRequestContextAccessor _contextAccessor = new FhirRequestContextAccessor();

        [Fact]
        public void GivenEnabledSortParameter_WhenValidating_ThenReturnsTrue()
        {
            SearchParameterInfo paramInfo = CreateSearchParameterInfo("name", "name", SearchParamType.String, SortParameterStatus.Enabled);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> sorting = [(paramInfo, SortOrder.Ascending)];

            var validator = new MongoDbSortingValidator(_contextAccessor);
            bool sortingValid = validator.ValidateSorting(sorting, out IReadOnlyList<string> errorMessage);

            Assert.True(sortingValid);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void GivenSupportedSortParameterWithoutPartialIndexHeader_WhenValidating_ThenReturnsFalse()
        {
            SearchParameterInfo paramInfo = CreateSearchParameterInfo("name", "name", SearchParamType.String, SortParameterStatus.Supported);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> sorting = [(paramInfo, SortOrder.Ascending)];

            _contextAccessor.RequestContext = CreateRequestContext(createPartialIndexHeader: false);
            var validator = new MongoDbSortingValidator(_contextAccessor);
            bool sortingValid = validator.ValidateSorting(sorting, out IReadOnlyList<string> errorMessage);

            Assert.False(sortingValid);
            Assert.NotEmpty(errorMessage);
        }

        [Fact]
        public void GivenSupportedSortParameterWithPartialIndexHeader_WhenValidating_ThenReturnsTrue()
        {
            SearchParameterInfo paramInfo = CreateSearchParameterInfo("name", "name", SearchParamType.String, SortParameterStatus.Supported);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> sorting = [(paramInfo, SortOrder.Descending)];

            _contextAccessor.RequestContext = CreateRequestContext(createPartialIndexHeader: true);
            var validator = new MongoDbSortingValidator(_contextAccessor);
            bool sortingValid = validator.ValidateSorting(sorting, out IReadOnlyList<string> errorMessage);

            Assert.True(sortingValid);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void GivenLastUpdatedSortParameter_WhenValidating_ThenReturnsTrue()
        {
            SearchParameterInfo paramInfo = CreateSearchParameterInfo(KnownQueryParameterNames.LastUpdated, KnownQueryParameterNames.LastUpdated, SearchParamType.Date, SortParameterStatus.Disabled);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> sorting = [(paramInfo, SortOrder.Ascending)];

            var validator = new MongoDbSortingValidator(_contextAccessor);
            bool sortingValid = validator.ValidateSorting(sorting, out IReadOnlyList<string> errorMessage);

            Assert.True(sortingValid);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void GivenUnsupportedSortParameter_WhenValidating_ThenReturnsFalse()
        {
            SearchParameterInfo paramInfo = CreateSearchParameterInfo("age", "age", SearchParamType.Number, SortParameterStatus.Disabled);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> sorting = [(paramInfo, SortOrder.Ascending)];

            var validator = new MongoDbSortingValidator(_contextAccessor);
            bool sortingValid = validator.ValidateSorting(sorting, out IReadOnlyList<string> errorMessage);

            Assert.False(sortingValid);
            Assert.NotEmpty(errorMessage);
        }

        [Fact]
        public void GivenMultipleSortParameters_WhenValidating_ThenReturnsFalse()
        {
            SearchParameterInfo firstParam = CreateSearchParameterInfo("name", "name", SearchParamType.String, SortParameterStatus.Enabled);
            SearchParameterInfo secondParam = CreateSearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, SortParameterStatus.Enabled);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> sorting =
            [
                (firstParam, SortOrder.Ascending),
                (secondParam, SortOrder.Descending),
            ];

            var validator = new MongoDbSortingValidator(_contextAccessor);
            bool sortingValid = validator.ValidateSorting(sorting, out IReadOnlyList<string> errorMessage);

            Assert.False(sortingValid);
            Assert.NotEmpty(errorMessage);
        }

        [Fact]
        public void GivenNoSortParameters_WhenValidating_ThenReturnsTrue()
        {
            var validator = new MongoDbSortingValidator(_contextAccessor);
            bool sortingValid = validator.ValidateSorting(Array.Empty<(SearchParameterInfo, SortOrder)>(), out IReadOnlyList<string> errorMessage);

            Assert.True(sortingValid);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void GivenInvalidSortDirection_WhenValidating_ThenReturnsFalse()
        {
            SearchParameterInfo paramInfo = CreateSearchParameterInfo("name", "name", SearchParamType.String, SortParameterStatus.Enabled);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> sorting = [(paramInfo, (SortOrder)99)];

            var validator = new MongoDbSortingValidator(_contextAccessor);
            bool sortingValid = validator.ValidateSorting(sorting, out IReadOnlyList<string> errorMessage);

            Assert.False(sortingValid);
            Assert.NotEmpty(errorMessage);
        }

        [Fact]
        public void GivenRepeatedSortParameter_WhenValidating_ThenReturnsFalse()
        {
            SearchParameterInfo paramInfo = CreateSearchParameterInfo("name", "name", SearchParamType.String, SortParameterStatus.Enabled);
            IReadOnlyList<(SearchParameterInfo, SortOrder)> sorting =
            [
                (paramInfo, SortOrder.Ascending),
                (paramInfo, SortOrder.Ascending),
            ];

            var validator = new MongoDbSortingValidator(_contextAccessor);
            bool sortingValid = validator.ValidateSorting(sorting, out IReadOnlyList<string> errorMessage);

            Assert.False(sortingValid);
            Assert.NotEmpty(errorMessage);
        }

        private static SearchParameterInfo CreateSearchParameterInfo(string name, string code, SearchParamType type, SortParameterStatus status)
        {
            return new SearchParameterInfo(name, code, type)
            {
                SortStatus = status,
            };
        }

        private static FhirRequestContext CreateRequestContext(bool createPartialIndexHeader)
        {
            Dictionary<string, StringValues> requestHeaders = new Dictionary<string, StringValues>();

            if (createPartialIndexHeader)
            {
                requestHeaders[KnownHeaders.PartiallyIndexedParamsHeaderName] = new StringValues("true");
            }

            return new FhirRequestContext(
                method: "GET",
                uriString: "https://example.com/Patient",
                baseUriString: "https://example.com",
                correlationId: "test",
                requestHeaders: requestHeaders,
                responseHeaders: new Dictionary<string, StringValues>());
        }
    }
}
