// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.MongoDb.Features.Storage;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Microsoft.Health.Fhir.MongoDb.Features.Search.Queries
{
    internal sealed class QueryAssembler
    {
        // find a better home for these at some point
        private readonly string _bsonAndOperator = "$and";
        private readonly string _bsonOrOperator = "$or";
        private readonly string _bsonNotOperator = "$not";
        private readonly string _bsonElemMatchOperator = "$elemMatch";

        private BsonDocument _inProcessFilter = new BsonDocument();
        private BsonArray _inProcessConditions = new BsonArray();
        private Stack<Tuple<MultiaryOperator, BsonArray>> _multiaryOperatorStack = new();
        private MultiaryOperator? _root = null;
        private int _negationCount = 0;

        public QueryAssembler()
        {
        }

        private List<BsonDocument> Filters { get; } = [];

        public void StartNewFilter()
        {
            _inProcessFilter = new BsonDocument();
            _inProcessConditions = new BsonArray();
        }

        public void PushMultiaryOperator(MultiaryOperator op)
        {
            if (_root == null)
            {
                _root = op;
                return;
            }

            _multiaryOperatorStack.Push(new Tuple<MultiaryOperator, BsonArray>(op, []));
        }

        public void PopMultiaryOperator()
        {
            // if stack is 0 we are at the bottom, and only the root remains

            if (_multiaryOperatorStack.Count > 0)
            {
                BsonDocument matchConditions = [];

                Tuple<MultiaryOperator, BsonArray> arr = _multiaryOperatorStack.Pop();

                if (arr.Item1 == MultiaryOperator.Or)
                {
                    matchConditions.Add(new BsonElement(_bsonOrOperator, arr.Item2));
                }
                else
                {
                    matchConditions.Add(new BsonElement(_bsonAndOperator, arr.Item2));
                }

                _inProcessConditions.Add(matchConditions);
            }
        }

        public void AddCondition(BsonDocument condition)
        {
            if (_negationCount > 0)
            {
                condition = new BsonDocument(
                    condition.Names.First(),
                    new BsonDocument(_bsonNotOperator, condition.Values.First()));
            }

            if (_multiaryOperatorStack.Count > 0)
            {
                _multiaryOperatorStack.Peek().Item2.Add(condition);
            }
            else
            {
                _inProcessConditions.Add(condition);
            }
        }

        public void IncrementNegation()
        {
            _negationCount++;
        }

        public void DecrementNegation()
        {
            _negationCount--;
        }

        public void CompleteFilter()
        {
            BsonDocument matchConditions = new BsonDocument();

            if (!_inProcessConditions.Any())
            {
                // what to do ?  warn, throw
                return;
            }

            foreach (BsonDocument condition in _inProcessConditions)
            {
                matchConditions.AddRange(condition);
            }

            _inProcessFilter = new BsonDocument(
                FieldNameConstants.SearchIndexes,
                new BsonDocument(_bsonElemMatchOperator, matchConditions));

            Filters.Add(_inProcessFilter);
        }

        public void AddFilter(BsonDocument filter)
        {
            Filters.Add(filter);
        }

        // Renders the filters into a BsonDocument that is submitted to the MongoEngine to
        // satisfy the query
        public BsonDocument RenderFilters()
        {
            BsonArray arr = [.. Filters];

            // NOTECJH: do not include deleted resource records
            // Would there be a need to make this configurable ?
            arr.Add(new BsonDocument(FieldNameConstants.IsDeleted, false));

            // $and is (should be) the _root
            return new BsonDocument(_bsonAndOperator, arr);
        }
    }
}
