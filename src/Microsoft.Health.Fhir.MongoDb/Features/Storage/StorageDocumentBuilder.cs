// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.MongoDb.Extensions;
using Microsoft.Health.Fhir.MongoDb.Features.Search;
using MongoDB.Bson;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.MongoDb.Features.Storage
{
    internal class StorageDocumentBuilder : IStorageDocumentBuilder
    {
        public BsonDocument BuildStorageDocument(
            ResourceWrapper resource,
            string versionId,
            ResourceWrapperOperation resourceExt,
            JObject resourceDocument)
        {
            var doc = new BsonDocument
                {
                    { FieldNameConstants.ResourceId, new BsonString(resource.ResourceId) },
                    { FieldNameConstants.ResourceType, new BsonString(resource.ResourceTypeName) },
                    { FieldNameConstants.Version, new BsonString(versionId) },
                    { FieldNameConstants.IsLatest, true },
                    { FieldNameConstants.IsDeleted, resourceExt.Wrapper.IsDeleted },
                    { FieldNameConstants.LastModified, new BsonDateTime(resource.LastModified.ToUniversalTime().UtcDateTime) },
                    { FieldNameConstants.Resource, resourceDocument.ToBsonDocument() },
                    { FieldNameConstants.SearchIndexes, GetSearchIndexes(resourceExt.Wrapper.SearchIndices) },
                };

            return doc;
        }

        private BsonArray GetSearchIndexes(IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            BsonArray indexes = new BsonArray();

            foreach (SearchIndexEntry entry in searchIndices)
            {
                indexes.Add(SearchIndexEntryBsonDocumentGenerator.Generate(entry));
            }

            return indexes;
        }
    }
}
