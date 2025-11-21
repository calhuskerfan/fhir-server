// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.MongoDb.Features.Search
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "WTF")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1852:Seal internal types", Justification = "WTF Part 2")]

    internal class MongoDbSearchParameterValidator : IDataStoreSearchParameterValidator
    {
        // TODOCJH: Need to expand on this eventually.
        // ValidateSearchParameter
        public bool ValidateSearchParameter(SearchParameterInfo searchParameter, out string errorMessage)
        {
            errorMessage = null;
            return true;
        }
    }
}
