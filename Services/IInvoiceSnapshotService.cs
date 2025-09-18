using System;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public sealed class BuildInvoiceResult
    {
        public int InvoiceId { get; set; }
        public string RefNumber { get; set; } = "";
        public string Status { get; set; } = "Ready";     // Ready | Error | Exported
        public string? ErrorMessage { get; set; }
    }

    public interface IInvoiceSnapshotService
    {
        Task<BuildInvoiceResult> BuildInvoiceSnapshotAsync(int sheetId, bool summed, CancellationToken ct = default);
        Task<(string Status, string? Error, string? QbTxnID, DateTime? LastAttemptAt)> GetStatusAsync(int sheetId, CancellationToken ct = default);
    }
}