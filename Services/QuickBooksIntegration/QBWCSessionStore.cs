using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Data;                    // for SqlDbType
using System.Threading;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;

using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Concurrent;
using RaymarEquipmentInventory.DTOs;


namespace RaymarEquipmentInventory.Services
{
    public sealed class QbwcSessionStore : IQBWCSessionStore
    {
        private readonly IAuditLogger _audit;
        private readonly IMemoryCache _cache;
        private static readonly ConcurrentDictionary<string, Guid> _ticketToRun = new();

        // iterator state per runId
        private static readonly ConcurrentDictionary<Guid, QbwcIteratorState> _iterators = new();

        public QbwcSessionStore(IAuditLogger audit, IMemoryCache cache)
        {
            _audit = audit;
            _cache = cache;
        }

        public async Task<Guid> StartSessionAsync(string qbwcUser, string? companyFile, CancellationToken ct = default)
        {
            var runId = Guid.NewGuid();
            // We don’t know the ticket yet; StartSession when authenticate lands is fine.
            await _audit.StartSessionAsync(runId, qbwcUser, companyFile, ticket: "(pending)", ct);
            return runId;
        }

        public async Task MapTicketAsync(string ticket, Guid runId, CancellationToken ct = default)
        {
            _ticketToRun[ticket] = runId;
            // Update the session row to store the real ticket value
            await _audit.LogMessageAsync(runId, "authenticate", "resp", message: $"ticket={ticket}", ct: ct);
        }

        public bool TryGetRunId(string ticket, out Guid runId) =>
            _ticketToRun.TryGetValue(ticket, out runId);

        public async Task SetClientVersionAsync(Guid runId, string? clientVersion, CancellationToken ct = default)
        {
            await _audit.LogMessageAsync(runId, "clientVersion", "req", message: clientVersion, ct: ct);
        }

        public async Task SetServerVersionAsync(Guid runId, string? serverVersion, CancellationToken ct = default)
        {
            await _audit.LogMessageAsync(runId, "serverVersion", "resp", message: serverVersion, ct: ct);
        }

        public QbwcIteratorState GetIterator(Guid runId)
        {
            if (_iterators.TryGetValue(runId, out var s)) return s;
            s = new QbwcIteratorState();
            _iterators[runId] = s;
            return s;
        }

        public void SetIterator(Guid runId, QbwcIteratorState state)
        {
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            _iterators[runId] = state;
        }

        public async Task EndSessionAsync(Guid runId, string? lastError = null, CancellationToken ct = default)
        {
            _iterators.TryRemove(runId, out _);
            // don’t try to reverse-map ticket; not needed here
            await _audit.EndSessionAsync(runId, lastError, ct);
        }
    }
}
