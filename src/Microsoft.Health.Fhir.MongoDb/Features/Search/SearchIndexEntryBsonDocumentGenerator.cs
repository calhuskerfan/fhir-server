// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.MongoDb.Features.Search
{
    internal static class SearchIndexEntryBsonDocumentGenerator
    {
        public static BsonDocument Generate(SearchIndexEntry entry)
        {
            EnsureArg.IsNotNull(entry, nameof(entry));

            string val = Newtonsoft.Json.JsonConvert.SerializeObject(entry.Value);

            BsonDocument valueDocument = BsonSerializer.Deserialize<BsonDocument>(val);

            /*
            Complete list, [x] indicates handled
            Number
            Date [x]
            String
            Token
            Reference
            Quantity [x]
            Uri
            Composite,
            Special
            */

            if (entry.SearchParameter.Type == ValueSets.SearchParamType.Date)
            {
                var dtsv = (DateTimeSearchValue)entry.Value;

                // NOTECJH:  For reference:
                // The Cosmos implementation does not have IsValidAsCompositeComponent, IsMin, or IsMax
                // The SQL Implementaion does
                // some more digging at some point
                valueDocument =
                [
                    new BsonElement(SearchValueConstants.DateTimeStartName, new BsonDateTime(dtsv.Start.DateTime)),
                    new BsonElement(SearchValueConstants.DateTimeEndName, new BsonDateTime(dtsv.End.DateTime)),
                    new BsonElement("IsValidAsCompositeComponent", new BsonBoolean(dtsv.IsValidAsCompositeComponent)),
                    new BsonElement("IsMin", new BsonBoolean(dtsv.IsMin)),
                    new BsonElement("IsMax", new BsonBoolean(dtsv.IsMax)),
                ];
            }

            if (entry.SearchParameter.Type == ValueSets.SearchParamType.Quantity)
            {
                var quantityDocument = new BsonDocument();

                var quantity = (QuantitySearchValue)entry.Value;

                if (quantity.System != null)
                {
                    quantityDocument.Add(SearchValueConstants.SystemName, quantity.System);
                }

                if (quantity.Code != null)
                {
                    quantityDocument.Add(SearchValueConstants.CodeName, quantity.Code);
                }

                // TODOCJH:  We are taking it on faith that if there is only a value, which is actually
                // not what fhir prescribes.  Is there something going on with trying to use range and
                // quantity ?
                if (quantity.Low == quantity.High)
                {
                    quantityDocument.Add(SearchValueConstants.QuantityName, quantity.Low);
                }

                quantityDocument.Add(SearchValueConstants.LowQuantityName, quantity.Low);
                quantityDocument.Add(SearchValueConstants.HighQuantityName, quantity.High);

                valueDocument = quantityDocument;
            }

            if (entry.SearchParameter.Type == ValueSets.SearchParamType.Composite)
            {
                // BUGCJH
                // entry.SearchParameter.Type
            }

            var ret = new BsonDocument
            {
                { "SearchParameter", GetSearchParameterDocument(entry) },
                { "Value", valueDocument },
            };

            return ret;
        }

        private static BsonDocument GetSearchParameterDocument(SearchIndexEntry entry)
        {
            string sp = Newtonsoft.Json.JsonConvert.SerializeObject(entry.SearchParameter);

            BsonDocument searchParams = BsonSerializer.Deserialize<BsonDocument>(sp);

            // TODOCJH:  Just some simple 'property trimming' for now
            searchParams.Remove("Description");
            searchParams.Remove("TargetResourceTypes");
            searchParams.Remove("BaseResourceTypes");
            searchParams.Remove("Expression");

            return searchParams;
        }
    }
}
