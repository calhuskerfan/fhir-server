// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Microsoft.Health.Abstractions.Features.Transactions;
using MongoDB.Driver;

namespace Microsoft.Health.Fhir.MongoDb.Features.Storage
{
    /// <summary>
    /// Owns a MongoDB client session with an open transaction. The session is exposed through
    /// <see cref="Current"/> so that merges that enlist in the transaction write through it.
    /// Disposing a scope that was not completed aborts the transaction.
    /// </summary>
    internal sealed class MongoDbTransactionScope : ITransactionScope
    {
        private static readonly AsyncLocal<IClientSessionHandle?> CurrentSession = new AsyncLocal<IClientSessionHandle?>();

        private readonly IClientSessionHandle _session;
        private bool _completed;
        private bool _disposed;

        public MongoDbTransactionScope(IClientSessionHandle session)
        {
            _session = session;
            _session.StartTransaction();
            CurrentSession.Value = session;
        }

        /// <summary>
        /// Gets the session of the ambient transaction, or null when none is open.
        /// </summary>
        internal static IClientSessionHandle? Current => CurrentSession.Value;

        public void Complete()
        {
            if (_completed || _disposed)
            {
                return;
            }

            _session.CommitTransaction();
            _completed = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentSession.Value = null;

            if (!_completed && _session.IsInTransaction)
            {
                _session.AbortTransaction();
            }

            _session.Dispose();
        }
    }
}
