// Services/IQbXmlLogger.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public interface IQbXmlLogger
    {
        Task<Guid> LogAsync(
            Guid runId,
            string direction,         // "req" | "resp"
            string phase,             // "sendRequestXML" | "receiveResponseXML" | "InvoiceAdd" | ...
            string? requestType,      // "InvoiceAddRq" | "ItemInventoryQueryRq" | ...
            string? companyFile,
            string? iteratorId,
            int? invoiceId,
            string? refNumber,
            int? statusCode,
            string? hresult,
            string? message,
            string? payloadXml,
            int? durationMs = null,
            CancellationToken ct = default);
    }
}
