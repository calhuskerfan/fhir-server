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
    /// JObjectExtensions
    /// </summary>
    internal static class JObjectExtensions
    {
        /// <summary>
        /// Converts a JObject to a BsonDocument
        /// </summary>
        /// <param name="obj">JObject</param>
        /// <returns>BsonDocument</returns>
        public static BsonDocument ToBsonDocument(this JObject obj)
        {
            // TODOCJH: this can be sped up at some point in the future
            // https://stackoverflow.com/questions/62080252/convert-newtosoft-jobject-directly-to-bsondocument

            var document = BsonSerializer.Deserialize<BsonDocument>(obj.ToString());
            return document;
        }

        public static BsonArray ToBsonArray(this JArray obj)
        {
            var document = BsonSerializer.Deserialize<BsonArray>(obj.ToString());
            return document;
        }
    }
}
