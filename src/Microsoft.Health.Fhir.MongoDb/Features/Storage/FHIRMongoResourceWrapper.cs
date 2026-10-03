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
            var isRawResourceMetaSet = true;

            var isHistory = entry.Contains(FieldNameConstants.IsLatest) && !entry[FieldNameConstants.IsLatest].ToBoolean();

            var lastModified = entry.Contains(FieldNameConstants.LastModified) && entry[FieldNameConstants.LastModified].IsValidDateTime
                ? new DateTimeOffset(entry[FieldNameConstants.LastModified].ToUniversalTime(), TimeSpan.Zero)
                : DateTimeOffset.UtcNow;

            if (!entry.TryGetValue(FieldNameConstants.Resource, out var resourceValue) || !resourceValue.IsBsonDocument)
            {
                throw new InvalidOperationException($"Stored document is missing the '{FieldNameConstants.Resource}' body.");
            }

            var isDeleted = entry.TryGetValue(FieldNameConstants.IsDeleted, out var deletedValue) && deletedValue.ToBoolean();
            var resourceDocument = resourceValue.AsBsonDocument;
            var resourceId = entry.Contains(FieldNameConstants.ResourceId)
                ? entry[FieldNameConstants.ResourceId].AsString
                : resourceDocument[FieldNameConstants.Id].AsString;
            var resourceType = entry.Contains(FieldNameConstants.ResourceType)
                ? entry[FieldNameConstants.ResourceType].AsString
                : resourceDocument[FieldNameConstants.ResourceType].AsString;
            var version = entry.Contains(FieldNameConstants.Version)
                ? entry[FieldNameConstants.Version].ToString()
                : resourceDocument.TryGetValue("meta", out var meta) && meta.IsBsonDocument && meta.AsBsonDocument.TryGetValue("versionId", out var metaVersion) && metaVersion.IsString
                    ? metaVersion.AsString
                    : "1";
            var rawResource = resourceDocument.ToJson();

            var resourceWrapper = new FHIRMongoResourceWrapper(
                resourceId,
                version,
                resourceType,
                new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
                null,
                lastModified,
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
