// Services/QbXmlLogger.cs
using Microsoft.EntityFrameworkCore;
using RaymarEquipmentInventory.Models;
using System.Security.Cryptography;
using System.Text;

namespace RaymarEquipmentInventory.Services
{
    public sealed class QbXmlLogger : IQbXmlLogger
    {
        private readonly RaymarInventoryDBContext _context;
        public QbXmlLogger(RaymarInventoryDBContext context) => _context = context;

        public async Task<Guid> LogAsync(
            Guid runId, string direction, string phase, string? requestType,
            string? companyFile, string? iteratorId, int? invoiceId, string? refNumber,
            int? statusCode, string? hresult, string? message, string? payloadXml,
            int? durationMs = null, CancellationToken ct = default)
        {
            var reqGuid = Guid.NewGuid();
            byte[]? hash = null;
            int? size = null;

            if (!string.IsNullOrEmpty(payloadXml))
            {
                var bytes = Encoding.UTF8.GetBytes(payloadXml);
                size = bytes.Length;
                hash = SHA256.HashData(bytes);
            }

            var row = new QbXmlLog
            {
                RunId = runId,
                RequestGuid = reqGuid,
                Direction = direction,
                Phase = phase,
                RequestType = requestType,
                CompanyFile = companyFile,
                IteratorId = iteratorId,
                InvoiceId = invoiceId,
                RefNumber = refNumber,
                StatusCode = statusCode,
                Hresult = hresult,
                Message = message,
                PayloadSha256 = hash,
                PayloadSizeBytes = size,
                DurationMs = durationMs,
                PayloadXml = payloadXml,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.QbXmlLogs.Add(row);
            await _context.SaveChangesAsync(ct);
            return reqGuid;
        }
    }
}
