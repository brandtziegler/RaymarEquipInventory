using RaymarEquipmentInventory.DTOs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{

        public interface IQBWCSessionStore
        {
            Task<Guid> StartSessionAsync(string qbwcUser, string? companyFile, CancellationToken ct = default);
            Task MapTicketAsync(string ticket, Guid runId, CancellationToken ct = default);
            bool TryGetRunId(string ticket, out Guid runId);

            Task SetClientVersionAsync(Guid runId, string? clientVersion, CancellationToken ct = default);
            Task SetServerVersionAsync(Guid runId, string? serverVersion, CancellationToken ct = default);

            QbwcIteratorState GetIterator(Guid runId);
            void SetIterator(Guid runId, QbwcIteratorState state);

            Task EndSessionAsync(Guid runId, string? lastError = null, CancellationToken ct = default);
        }


}
