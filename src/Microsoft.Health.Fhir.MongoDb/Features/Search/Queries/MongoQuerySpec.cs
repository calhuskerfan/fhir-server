// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Microsoft.Health.Fhir.MongoDb.Features.Search.Queries
{
    internal sealed record MongoQuerySpec(
        BsonDocument Filter,
        SortDefinition<BsonDocument> Sort);
}
