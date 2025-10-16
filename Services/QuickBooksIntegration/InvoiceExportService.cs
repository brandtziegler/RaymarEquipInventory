using Microsoft.EntityFrameworkCore;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    /// Builds InvoiceAdd payloads and updates invoice export metadata.
    public sealed class InvoiceExportService : IInvoiceExportService
    {
        private readonly RaymarInventoryDBContext _context;

        public InvoiceExportService(RaymarInventoryDBContext context)
        {
            _context = context;
        }

        public async Task<InvoiceAddPayload> BuildInvoiceAddPayloadAsync(int invoiceId, CancellationToken ct = default)
        {
            // Header
            var inv = await _context.Invoices
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId, ct)
                ?? throw new InvalidOperationException($"Invoice {invoiceId} not found.");

            if (string.IsNullOrWhiteSpace(inv.CustomerListId))
                throw new InvalidOperationException("CustomerListID is required for InvoiceAdd.");

            // Lines (ordered)
            var lines = await _context.InvoiceLines
                .AsNoTracking()
                .Where(l => l.InvoiceId == invoiceId)
                .OrderBy(l => l.LineNumber)
                .ToListAsync(ct);

            // Resolve HST ListID (prefer keys table if you have it; fallback to staging)
            var hstListId = await ResolveHstListIdAsync(ct);

            var payload = new InvoiceAddPayload
            {
                CustomerListID = inv.CustomerListId!,
                RefNumber = inv.RefNumber ?? $"WO-{inv.WorkOrderId}",
                TxnDate = inv.TxnDate,
                PONumber = inv.Ponumber,
                Memo = inv.Memo,
                ItemSalesTaxRefListID = hstListId
            };

            foreach (var l in lines)
            {
                // Do NOT send a dedicated “Tax” line — header ItemSalesTaxRef handles tax.
                if (string.Equals(l.SourceType, "Tax", StringComparison.OrdinalIgnoreCase))
                    continue;

                payload.Lines.Add(new InvoiceAddLine
                {
                    ItemListID = l.ItemListId,                                           // should be hydrated by your view
                    Desc = string.IsNullOrWhiteSpace(l.Description) ? l.ItemNameSnapshot : l.Description,
                    Qty = l.Qty,
                    Rate = l.Rate,
                    ClassRef = l.ClassRef,
                    ServiceDate = l.ServiceDate,
                    IsTaxable = l.IsTaxable
                });
            }

            return payload;
        }

        public async Task<int?> GetNextPendingInvoiceIdAsync(CancellationToken ct = default)
        {
            return await _context.Invoices
                .Where(i => i.Status == "Ready")
                .OrderBy(i => i.InvoiceId)
                .Select(i => (int?)i.InvoiceId)
                .FirstOrDefaultAsync(ct);
        }


        public async Task OnInvoiceExportSuccessAsync(int invoiceId, string txnId, string editSeq, CancellationToken ct = default)
        {
            var inv = await _context.Invoices.FirstAsync(i => i.InvoiceId == invoiceId, ct);
            inv.QbTxnId = txnId;
            inv.QbEditSequence = editSeq;
            inv.Status = "Exported";
            inv.ErrorMessage = null;
            inv.LastExportAttemptAt = DateTime.UtcNow;
            inv.ExportedAt = DateTime.UtcNow;
            inv.UpdatedAt = DateTime.UtcNow;
            inv.ExportAttemptCount = (inv.ExportAttemptCount <= 0 ? 1 : inv.ExportAttemptCount + 1);
            await _context.SaveChangesAsync(ct);
        }

        public async Task OnInvoiceExportFailureAsync(int invoiceId, string error, CancellationToken ct = default)
        {
            var inv = await _context.Invoices.FirstAsync(i => i.InvoiceId == invoiceId, ct);
            inv.Status = "Ready";                         // keep ready so it can retry; change to "Error" if you prefer a manual reset
            inv.ErrorMessage = error?.Trim();
            inv.LastExportAttemptAt = DateTime.UtcNow;
            inv.UpdatedAt = DateTime.UtcNow;
            inv.ExportAttemptCount = (inv.ExportAttemptCount <= 0 ? 1 : inv.ExportAttemptCount + 1);
            await _context.SaveChangesAsync(ct);
        }

        // ---------- helpers ----------
        private async Task<string?> ResolveHstListIdAsync(CancellationToken ct)
        {
            // Preferred: a small keys table (if present)
            var keysExists = await _context.Database
                .ExecuteSqlRawAsync("SELECT 1") // placeholder ping; swap to a proper check if you like
                .ContinueWith(_ => _context.GetType().GetProperties().Any(p => p.Name.Equals("QuickBooksKeys", StringComparison.OrdinalIgnoreCase)));

            if (keysExists)
            {
                // dynamic check via LINQ over a likely DbSet<QuickBooksKey>
                var keysProp = _context.GetType().GetProperties().FirstOrDefault(p => p.Name.Equals("QuickBooksKeys", StringComparison.OrdinalIgnoreCase));
                if (keysProp?.GetValue(_context) is IQueryable<object> keysQuery)
                {
                    // Expect properties: Key, ListID
                    var listId = await keysQuery
                        .Where(k => EF.Property<string>(k, "Key") == "Item_Tax_HST")
                        .Select(k => EF.Property<string>(k, "ListID"))
                        .Cast<string?>()
                        .FirstOrDefaultAsync(ct);
                    if (!string.IsNullOrWhiteSpace(listId))
                        return listId!;
                }
            }

            // Fallback: the staging table (if mapped as DbSet)
            var otherExists = _context.GetType().GetProperties().Any(p => p.Name.Equals("QBItemOtherStagings", StringComparison.OrdinalIgnoreCase));
            if (otherExists)
            {
                var setProp = _context.GetType().GetProperty("QBItemOtherStagings");
                if (setProp?.GetValue(_context) is IQueryable<object> otherQuery)
                {
                    var listId = await otherQuery
                        .Where(x => EF.Property<string>(x, "Type") == "SalesTaxItem" &&
                                    EF.Property<string>(x, "Name").StartsWith("HST"))
                        .Select(x => EF.Property<string>(x, "ListID"))
                        .Cast<string?>()
                        .FirstOrDefaultAsync(ct);
                    if (!string.IsNullOrWhiteSpace(listId))
                        return listId!;
                }
            }

            // Nothing found — allow null; QuickBooks will reject if header tax is required.
            return null;
        }
    }
}

