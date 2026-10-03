// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace Microsoft.Health.Fhir.MongoDb.UnitTests.Features.Storage
{
    public sealed class MongoDbTestFixture : IAsyncLifetime
    {
        private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0")
            .WithReplicaSet()
            .Build();

        private MongoClient? _client;

        public string ConnectionString => _container.GetConnectionString();

        public async ValueTask InitializeAsync()
        {
            await _container.StartAsync();
            _client = new MongoClient(ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await _container.DisposeAsync();
        }

        public Task ResetAsync()
        {
            return GetCollection().DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
        }

        public Task InsertAsync(BsonDocument document)
        {
            return GetCollection().InsertOneAsync(document);
        }

        public Task<List<BsonDocument>> GetResourceDocumentsAsync(string resourceType, string resourceId)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("resourceType", resourceType) &
                Builders<BsonDocument>.Filter.Eq("resourceId", resourceId);

            return GetCollection().Find(filter).ToListAsync();
        }

        public async Task SeedAsync(
            string resourceType,
            string resourceId,
            string version,
            bool isLatest,
            bool isDeleted = false,
            DateTimeOffset? lastModified = null,
            string? additionalField = null)
        {
            var modified = lastModified ?? new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero);
            var resource = new BsonDocument
            {
                { "resourceType", resourceType },
                { "id", resourceId },
                {
                    "meta",
                    new BsonDocument
                    {
                        { "versionId", version },
                        { "lastUpdated", modified.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") },
                    }
                },
            };

            var document = new BsonDocument
            {
                { "resourceId", resourceId },
                { "resourceType", resourceType },
                { "version", version },
                { "isLatest", isLatest },
                { "isDeleted", isDeleted },
                { "lastModified", new BsonDateTime(modified.UtcDateTime) },
                { "resource", resource },
                { "searchIndexes", new BsonArray() },
            };

            if (additionalField != null)
            {
                document.Add("additionalField", additionalField);
            }

            await InsertAsync(document);
        }

        private IMongoCollection<BsonDocument> GetCollection()
        {
            return (_client ?? throw new InvalidOperationException("The MongoDB test fixture has not been initialized."))
                .GetDatabase("fhir")
                .GetCollection<BsonDocument>("resource");
        }
    }
}
