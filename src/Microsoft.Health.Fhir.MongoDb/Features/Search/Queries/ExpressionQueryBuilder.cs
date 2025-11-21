// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel.Types;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Health.Fhir.Api;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.MongoDb.Features.Storage;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Microsoft.Health.Fhir.MongoDb.Features.Search.Queries
{
    internal sealed class ExpressionQueryBuilder : IExpressionVisitorWithInitialContext<ExpressionQueryBuilderContext, object?>
    {
        private static readonly Dictionary<BinaryOperator, string> BinaryOperatorMapping = new()
        {
            { BinaryOperator.Equal, "$eg" },
            { BinaryOperator.GreaterThan, "$gt" },
            { BinaryOperator.GreaterThanOrEqual, "$gte" },
            { BinaryOperator.LessThan, "$lt" },
            { BinaryOperator.LessThanOrEqual, "$lte" },
            { BinaryOperator.NotEqual, "$ne" },
        };

        private static readonly Dictionary<FieldName, string> FieldNameMapping = new()
        {
            { FieldName.DateTimeEnd, SearchValueConstants.DateTimeEndName },
            { FieldName.DateTimeStart, SearchValueConstants.DateTimeStartName },
            { FieldName.Number, SearchValueConstants.NumberName },
            { FieldName.ParamName, SearchValueConstants.ParamName },
            { FieldName.Quantity, SearchValueConstants.QuantityName },
            { FieldName.QuantityCode, SearchValueConstants.CodeName },
            { FieldName.QuantitySystem, SearchValueConstants.SystemName },
            { FieldName.ReferenceBaseUri, SearchValueConstants.ReferenceBaseUriName },
            { FieldName.ReferenceResourceId, SearchValueConstants.ReferenceResourceIdName },
            { FieldName.ReferenceResourceType, SearchValueConstants.ReferenceResourceTypeName },
            { FieldName.String, SearchValueConstants.StringName },
            { FieldName.TokenCode, SearchValueConstants.CodeName },
            { FieldName.TokenSystem, SearchValueConstants.SystemName },
            { FieldName.TokenText, SearchValueConstants.TextName },
            { FieldName.Uri, SearchValueConstants.UriName },
        };

        public ExpressionQueryBuilderContext InitialContext => new ExpressionQueryBuilderContext();

        // generates
        public object? VisitBinary(BinaryExpression expression, ExpressionQueryBuilderContext context)
        {
            string field = $"Value.{GetFieldName(expression)}";

            BsonValue value;

            if (expression.Value is decimal dv)
            {
                value = BsonDecimal128.Create(dv);
            }
            else if (expression.Value is System.DateTimeOffset)
            {
                var dto = (System.DateTimeOffset)expression.Value;
                value = new BsonDateTime(dto.DateTime);
            }
            else if (expression.Value is string sv)
            {
                value = sv;
            }
            else
            {
                throw new NotSupportedException($"{expression.Value}");
            }

            context.Assembler.AddCondition(new BsonDocument(
                field,
                new BsonDocument(
                    GetMappedValue(BinaryOperatorMapping, expression.BinaryOperator),
                    value)));

            return null;
        }

        public object? VisitMultiary(MultiaryExpression expression, ExpressionQueryBuilderContext context)
        {
            IReadOnlyList<Expression> expressions = expression.Expressions;

            context.Assembler.PushMultiaryOperator(expression.MultiaryOperation);

            for (int i = 0; i < expressions.Count; i++)
            {
                expressions[i].AcceptVisitor(this, context);
            }

            context.Assembler.PopMultiaryOperator();

            return null;
        }

        public object? VisitSearchParameter(SearchParameterExpression expression, ExpressionQueryBuilderContext context)
        {
            switch (expression.Parameter.Code)
            {
                case SearchParameterNames.ResourceType:
                    context.Assembler
                        .AddFilter(
                        new BsonDocument(
                            $"{FieldNameConstants.Resource}.{FieldNameConstants.ResourceType}",
                            ((StringExpression)expression.Expression).Value));

                    break;
                case SearchParameterNames.Id:
                    // expression.Expression.AcceptVisitor(this, context.WithFieldNameOverride((n, i) => KnownResourceWrapperProperties.ResourceId));
                    throw new NotImplementedException();
                case SearchParameterNames.LastUpdated:
                    // For LastUpdate queries, the LastModified property on the root is
                    // more performant than the searchIndices _lastUpdated.st and _lastUpdate.et
                    // we will override the mapping for that
                    // expression.Expression.AcceptVisitor(this, context.WithFieldNameOverride((n, i) => SearchValueConstants.LastModified));
                    throw new NotImplementedException();
                case SearchValueConstants.WildcardReferenceSearchParameterName:
                    // This is an internal search parameter that matches any reference search parameter.
                    // It is used for wildcard revinclude queries
                    throw new NotImplementedException();
                default:
                    AppendFilter(expression, context);
                    break;
            }

            return null;
        }

        // AppendSubquery
        private void AppendFilter(
            SearchParameterExpression expression,
            ExpressionQueryBuilderContext context)
        {
            // CJH: We need to look at the expression.Parameter.Type ?

            context.Assembler.StartNewFilter();

            if (expression.Expression != null)
            {
                context.Assembler.AddCondition(
                    new BsonDocument(
                        $"{FieldNameConstants.SearchParameter}.{FieldNameConstants.SearchParameterCode}",
                        expression.Parameter.Code));

                expression.Expression.AcceptVisitor(this, context);
            }

            context.Assembler.CompleteFilter();
        }

        // GetFieldName
        private static string GetFieldName(IFieldExpression field)
        {
            if (FieldNameMapping.TryGetValue(field.FieldName, out string? value))
            {
                // TODOCJH:  Can we get here ?  We would not have a field name with a null in the dictionary
                if (value == null)
                {
                    throw new InvalidOperationException();
                }

                return value;
            }

            throw new InvalidOperationException();
        }

        // VisitString
        public object? VisitString(StringExpression expression, ExpressionQueryBuilderContext context)
        {
            string field = $"Value.{GetFieldName(expression)}";

            BsonValue? value = null;

            if (expression.StringOperator == StringOperator.StartsWith)
            {
                value = new BsonRegularExpression("^" + expression.Value + ".*");
            }
            else if (expression.StringOperator == StringOperator.Equals)
            {
                /*
                 * TODOCJH: at some point do we want to look at a string version of BinaryOperatorMapping
                 * not sure how it fits into the string operator, but would remove the '$eq' literal
                */

                value = new BsonDocument("$eq", expression.Value);
            }
            else if (expression.StringOperator == StringOperator.Contains)
            {
                value = new BsonRegularExpression("/" + expression.Value + "/");
            }
            else
            {
                /*
                EndsWith,
                NotContains,
                NotEndsWith,
                NotStartsWith,
                LeftSideStartsWith,
                */

                throw new InvalidOperationException($"{expression.StringOperator}");
            }

            context.Assembler.
                AddCondition(new BsonDocument(field, value));

            return null;
        }

        // VisitNotExpression
        // TODOCJH: Verify that this can handle nesting.
        public object? VisitNotExpression(NotExpression expression, ExpressionQueryBuilderContext context)
        {
            context.Assembler.IncrementNegation();

            expression.Expression.AcceptVisitor(this, context);

            context.Assembler.DecrementNegation();

            return null;
        }

        public object VisitUnion(UnionExpression expression, ExpressionQueryBuilderContext context)
        {
            throw new NotImplementedException();
        }

        public object VisitChained(ChainedExpression expression, ExpressionQueryBuilderContext context)
        {
            throw new NotImplementedException();
        }

        public object VisitCompartment(CompartmentSearchExpression expression, ExpressionQueryBuilderContext context)
        {
            throw new NotImplementedException();
        }

        public object VisitIn<T>(InExpression<T> expression, ExpressionQueryBuilderContext context)
        {
            throw new NotImplementedException();
        }

        public object VisitInclude(IncludeExpression expression, ExpressionQueryBuilderContext context)
        {
            throw new InvalidOperationException($"Include expression should have been removed before reaching {nameof(ExpressionQueryBuilder)}.");
        }

        public object VisitMissingField(MissingFieldExpression expression, ExpressionQueryBuilderContext context)
        {
            throw new NotImplementedException();
        }

        public object? VisitMissingSearchParameter(MissingSearchParameterExpression expression, ExpressionQueryBuilderContext context)
        {
            if (expression.Parameter.Code == SearchParameterNames.ResourceType)
            {
                throw new NotImplementedException();
            }

            var arr = new BsonArray
            {
                new BsonString(expression.Parameter.Code),
            };

            context.Assembler.AddFilter(
                new BsonDocument(
                    $"{FieldNameConstants.SearchIndexes}.{FieldNameConstants.SearchParameter}.{FieldNameConstants.SearchParameterCode}",
                    new BsonDocument("$nin", arr)));

            return null;
        }

        public object? VisitSmartCompartment(SmartCompartmentSearchExpression expression, ExpressionQueryBuilderContext context)
        {
            throw new NotImplementedException();
        }

        public object? VisitSortParameter(SortExpression expression, ExpressionQueryBuilderContext context)
        {
            throw new NotImplementedException();
        }

        public object? VisitNotReferenced(NotReferencedExpression expression, ExpressionQueryBuilderContext context)
        {
            throw new NotImplementedException();
        }

        private static string GetMappedValue<T>(Dictionary<T, string> mapping, T key)
        {
            if (mapping.TryGetValue(key, out string value))
            {
                return value;
            }

            string message = string.Format("Unhandled {0} '{1}'.", typeof(T).Name, key);

            Debug.Fail(message);

            throw new InvalidOperationException(message);
        }
    }
}
