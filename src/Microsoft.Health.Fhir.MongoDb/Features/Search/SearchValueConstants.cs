// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.MongoDb.Features.Search
{
    public static class SearchValueConstants
    {
        public const string RootAliasName = "r";

        public const string RootResourceTypeName = "resourceTypeName";

        public const string SearchIndexAliasName = "si";

        public const string ParamName = "p";

        public const string DateTimeStartName = "Start";

        public const string DateTimeEndName = "End";

        public const string NumberName = "n";

        public const string LowNumberName = "ln";

        public const string HighNumberName = "hn";

        public const string NormalizedPrefix = "n_";

        public const string NormalizedStringName = NormalizedPrefix + StringName;

        public const string NormalizedTextName = NormalizedPrefix + TextName;

        // TODOCJH: Is this what we want ?
        // previously Cheated from SQL [FHIR].[dbo].[QuantitySearchParam] table, but
        // now going back to quantity
        public const string QuantityName = "Quantity";

        public const string LowQuantityName = "Low";

        public const string HighQuantityName = "High";

        public const string SystemName = "System";

        public const string CodeName = "Code";

        public const string ReferenceBaseUriName = "rb";

        public const string ReferenceResourceTypeName = "ResourceType";

        public const string ReferenceResourceIdName = "ResourceId";

        public const string StringName = "String";

        public const string TextName = "t";

        public const string UriName = "u";

        public const string LastModified = "lastModified";

        public const string SelectedFields = "r.id,r.isSystem,r.partitionKey,r.lastModified,r.rawResource,r.request,r.isDeleted,r.resourceId,r.resourceTypeName,r.isHistory,r.version,r._self,r._etag, r.searchParameterHash";

        public const string WildcardReferenceSearchParameterName = "_wildcardReference";

        public const string SortLowValueFieldName = "l";

        public const string SortHighValueFieldName = "h";
    }
}
