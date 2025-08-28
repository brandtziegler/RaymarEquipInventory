using RaymarEquipmentInventory.DTOs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{

        public interface IAuditLogger
        {
            Task StartSessionAsync(Guid runId, string? qbwcUser, string? companyFile, string ticket, CancellationToken ct = default);
            Task EndSessionAsync(Guid runId, string? lastError = null, CancellationToken ct = default);

            Task LogMessageAsync(
                Guid runId,
                string method,          // authenticate, sendRequestXML, receiveResponseXML, etc.
                string direction,       // "req" or "resp"
                int? statusCode = null, // e.g., 100 or 0 from receiveResponseXML
                string? hresult = null,
                string? message = null,
                string? companyFile = null,
                string? payloadXml = null,
                CancellationToken ct = default);
        }

}
