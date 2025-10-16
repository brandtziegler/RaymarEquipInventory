using RaymarEquipmentInventory.DTOs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public interface IInvoiceExportService
    {
        /// Build a qbXML-ready payload from DB for the given invoice.
        Task<InvoiceAddPayload> BuildInvoiceAddPayloadAsync(int invoiceId, CancellationToken ct = default);

        /// Mark success (store TxnID/EditSequence, flip status to Exported).
        Task OnInvoiceExportSuccessAsync(int invoiceId, string txnId, string editSeq, CancellationToken ct = default);

        /// Optional helper to record an export failure and bump attempts.
        Task OnInvoiceExportFailureAsync(int invoiceId, string error, CancellationToken ct = default);

        Task<int?> GetNextPendingInvoiceIdAsync(CancellationToken ct = default);
    }
}
