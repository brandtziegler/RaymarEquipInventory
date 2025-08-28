using RaymarEquipmentInventory.DTOs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public interface IQBWCResponseHandler
    {
        Task<ReceiveParseResult> HandleReceiveAsync(
            Guid runId,
            string responseXml,
            string? hresult,
            string? message,
            CancellationToken ct = default);
    }
}