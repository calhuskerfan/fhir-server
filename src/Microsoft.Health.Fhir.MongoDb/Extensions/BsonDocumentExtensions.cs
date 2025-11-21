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
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.MongoDb.Extensions
{
    /// <summary>
    /// Extensions for BsonDocuments
    /// this needs some work but is a start.
    /// </summary>
    internal static class BsonDocumentExtensions
    {
        public static JObject ToJObject(this BsonDocument document)
        {
            return JObject.Parse(document.ToJson());
        }

        public static JToken ToJToken(this BsonDocument document)
        {
            return JToken.Parse(document.ToJson());
        }
    }
}
