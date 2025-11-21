// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.MongoDb.Features.Storage
{
    public static class FieldNameConstants
    {
        public const string Resource = "resource";
        public const string Id = "id";
        public const string ResourceType = "resourceType";
        public const string IsDeleted = "isDeleted";
        public const string SearchIndexes = "searchIndexes";
        public const string SearchParameter = "SearchParameter";
        public const string SearchParameterCode = "Code";
    }
}
