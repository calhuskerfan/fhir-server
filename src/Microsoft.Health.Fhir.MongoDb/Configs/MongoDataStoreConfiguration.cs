// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Microsoft.Health.Fhir.MongoDb.Configs
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The MongoClient is intentionally shared for the process lifetime.")]
    public class MongoDataStoreConfiguration
    {
        private readonly string _collectionName = "resource";
        private readonly string _databaseName = "fhir";
        private readonly object _clientLock = new object();
        private IMongoClient? _client;
        private string? _clientConnectionString;

        public string? ConnectionString { get; set; }

        public IMongoCollection<BsonDocument> GetCollection(string? resourceTypeName = null)
        {
            /*
             * TODOCJH:  The idea here is that we could support different collections for different
             * resource types, but for now we are putting everything into one.  At some point we could also
             * use this as a base for partitioning, etc.
             */

            string collectionName = _collectionName;

            return GetClient()
                .GetDatabase(_databaseName)
                .GetCollection<BsonDocument>(collectionName.ToLowerInvariant());
        }

        /// <summary>
        /// Gets the shared client. Sessions and the collections they write to must come from the same client.
        /// </summary>
        /// <returns>The shared <see cref="IMongoClient"/>.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Lazily creates the client.")]
        public IMongoClient GetClient()
        {
            var connectionString = ConnectionString;

            lock (_clientLock)
            {
                if (_client == null || _clientConnectionString != connectionString)
                {
                    _client = new MongoClient(connectionString);
                    _clientConnectionString = connectionString;
                }

                return _client;
            }
        }
    }
}
