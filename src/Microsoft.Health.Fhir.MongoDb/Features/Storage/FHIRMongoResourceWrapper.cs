// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using MongoDB.Bson;

namespace Microsoft.Health.Fhir.MongoDb.Features.Storage
{
    internal sealed class FHIRMongoResourceWrapper : ResourceWrapper
    {
        public FHIRMongoResourceWrapper(
            string resourceId,
            string versionId,
            string resourceTypeName,
            RawResource rawResource,
            ResourceRequest request,
            DateTimeOffset lastModified,
            bool deleted,
            bool history,
            IReadOnlyCollection<SearchIndexEntry> searchIndices,
            CompartmentIndices compartmentIndices,
            IReadOnlyCollection<KeyValuePair<string, string>> lastModifiedClaims,
            string? searchParameterHash = null)
            : base(resourceId, versionId, resourceTypeName, rawResource, request, lastModified, deleted, searchIndices, compartmentIndices, lastModifiedClaims, searchParameterHash)
        {
            IsHistory = history;
        }

        public static FHIRMongoResourceWrapper FromBsonDocument(BsonDocument entry)
        {
            var version = 1;
            var isHistory = false;
            var isRawResourceMetaSet = true;

            var isDeleted = entry[FieldNameConstants.IsDeleted].ToBoolean();

            string rawResource = entry[FieldNameConstants.Resource].ToString();

            var resourceWrapper = new FHIRMongoResourceWrapper(
                entry[FieldNameConstants.Resource][FieldNameConstants.Id].ToString(),
                version.ToString(CultureInfo.InvariantCulture),
                entry[FieldNameConstants.Resource][FieldNameConstants.ResourceType].ToString(),
                new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
                null,
                DateTimeOffset.Now,
                isDeleted,
                isHistory,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: null);

            return resourceWrapper;
        }
    }
}
