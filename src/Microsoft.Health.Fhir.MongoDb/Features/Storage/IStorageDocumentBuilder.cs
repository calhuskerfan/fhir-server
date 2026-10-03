// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using MongoDB.Bson;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.MongoDb.Features.Storage
{
    public interface IStorageDocumentBuilder
    {
        BsonDocument BuildStorageDocument(
            ResourceWrapper resource,
            string versionId,
            ResourceWrapperOperation resourceExt,
            JObject resourceDocument);
    }
}
