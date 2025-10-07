using Microsoft.EntityFrameworkCore;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Helpers;
using RaymarEquipmentInventory.Models;
using System.Security.Cryptography;
using System.Text;

namespace RaymarEquipmentInventory.Services
{
    /// Builds an idempotent Invoice + InvoiceLines snapshot using vw_InvoicePreviewWithQBInv.
    public sealed class InvoiceSnapshotService : IInvoiceSnapshotService
    {
        private readonly RaymarInventoryDBContext _context;
        private readonly IReportingService _reporting; // kept for possible reuse; not required here

        public InvoiceSnapshotService(RaymarInventoryDBContext context, IReportingService reporting)
        {
            _context = context;
            _reporting = reporting;
        }

        public async Task<BuildInvoiceResult> BuildInvoiceSnapshotAsync(int sheetId, bool summed, CancellationToken ct = default)
        {
            static string CleanDesc(string? s) =>
                string.IsNullOrWhiteSpace(s) ? "" : s.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ").Trim();
            static string CleanTech(string? s) =>
                string.IsNullOrWhiteSpace(s) ? "N/A" : s.Trim();

            BuildInvoiceResult Error(string msg)
            {
                Serilog.Log.Error("InvoiceSnapshot FAIL sheetId={SheetId}: {Msg}", sheetId, msg);
                return new BuildInvoiceResult { Status = "Error", ErrorMessage = msg };
            }

            try
            {
                // 1) Source rows (from view)
                List<Models.VwInvoicePreviewWithQbinv> rows;
                try
                {
                    rows = await _context.VwInvoicePreviewWithQbinvs
                        .Where(v => v.SheetId == sheetId)
                        .AsNoTracking()
                        .ToListAsync(ct);
                }
                catch (Exception ex)
                {
                    return Error($"Failed reading vw_InvoicePreviewWithQBInv: {ex.GetBaseException().Message}");
                }
                if (rows.Count == 0) return Error($"No invoice data found for SheetID {sheetId}.");

                // 2) WO + customer
                var wo = await _context.WorkOrderSheets.AsNoTracking()
                             .FirstOrDefaultAsync(w => w.SheetId == sheetId, ct);
                if (wo == null) return Error($"Work order not found for SheetID {sheetId}.");

                var any = rows[0];
                var customerListId = any.CustomerListId;
                var customerId = any.CustomerId;
                if (string.IsNullOrWhiteSpace(customerListId))
                    return Error("Customer ListID missing on export view.");

                // 3) Ref/date
                var woNumber = (wo.WorkOrderNumber != 0) ? wo.WorkOrderNumber : sheetId;
                var refNumber = $"WO-{woNumber}";
                DateTime invoiceDate;
                try
                {
                    invoiceDate = TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")).Date;
                }
                catch { invoiceDate = DateTime.UtcNow.Date; }

                // 4) Config + totals
                IIFConfig cfg;
                try { cfg = IIFConfigHelper.GetIifConfig(); }
                catch (Exception ex) { return Error($"Failed to load IIF config: {ex.GetBaseException().Message}"); }

                const int MONEY2 = 2;
                decimal subtotal = rows.Sum(r => Math.Round((decimal)r.TotalAmount, MONEY2, MidpointRounding.AwayFromZero));
                bool needTax = subtotal != 0m && cfg.HstRate != 0m;
                decimal tax = Math.Round(subtotal * cfg.HstRate, MONEY2, MidpointRounding.AwayFromZero);
                decimal total = subtotal + tax;

                // fallback for parts missing ListID
                var miscPartListId = await _context.InventoryData
                    .Where(i => i.ItemName == cfg.MiscPartItem)
                    .Select(i => i.QuickBooksInvId)
                    .FirstOrDefaultAsync(ct);

                await using var tx = await _context.Database.BeginTransactionAsync(ct);

                var existing = await _context.Invoices
                    .Include(h => h.InvoiceLines)
                    .FirstOrDefaultAsync(i => i.RefNumber == refNumber, ct);

                if (existing != null && string.Equals(existing.Status, "Exported", StringComparison.OrdinalIgnoreCase))
                    return new BuildInvoiceResult
                    {
                        InvoiceId = existing.InvoiceId,
                        RefNumber = existing.RefNumber,
                        Status = existing.Status,
                        ErrorMessage = "Invoice already exported; cannot rebuild."
                    };

                // header fields
                var poNum = await _context.BillingInformations.AsNoTracking()
                                .Where(b => b.SheetId == sheetId)
                                .Select(b => b.Pono)
                                .FirstOrDefaultAsync(ct);
                var memo = await _context.BillingInformations.AsNoTracking()
                                .Where(b => b.SheetId == sheetId)
                                .Select(b => b.WorkDescription)
                                .FirstOrDefaultAsync(ct);

                Models.Invoice inv;
                if (existing == null)
                {
                    inv = new Models.Invoice
                    {
                        WorkOrderId = wo.SheetId,
                        CustomerId = customerId,
                        CustomerListId = customerListId,
                        RefNumber = refNumber,
                        TxnDate = invoiceDate,
                        Subtotal = subtotal,
                        TaxAmount = tax,
                        Total = total,
                        Ponumber = poNum,
                        Memo = memo,
                        Currency = "CAD",
                        Status = "Ready",
                        SourceHash = ComputeSourceHash(rows),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.Invoices.Add(inv);
                    await _context.SaveChangesAsync(ct);
                }
                else
                {
                    inv = existing;
                    inv.CustomerId = customerId;
                    inv.CustomerListId = customerListId;
                    inv.Subtotal = subtotal;
                    inv.TaxAmount = tax;
                    inv.Total = total;
                    inv.Status = "Ready";
                    inv.Memo = memo;
                    inv.Ponumber = poNum;
                    inv.UpdatedAt = DateTime.UtcNow;
                    inv.SourceHash = ComputeSourceHash(rows);

                    _context.InvoiceLines.RemoveRange(inv.InvoiceLines);
                    await _context.SaveChangesAsync(ct);
                }

                // Build invoice lines
                var pending = new List<Models.InvoiceLine>();
                int lineNo = 1;
                foreach (var r in rows
                    .OrderBy(x => x.Category)
                    .ThenBy(x => x.TechnicianName)
                    .ThenBy(x => x.ItemName))
                {
                    string itemNameSnapshot = r.ItemName ?? "";
                    string? listId = r.QuickBooksInvId;

                    if (string.Equals(r.Category, "PartsUsed", StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(listId) &&
                        !string.IsNullOrWhiteSpace(miscPartListId))
                    {
                        itemNameSnapshot = cfg.MiscPartItem;
                        listId = miscPartListId;
                    }

                    pending.Add(new Models.InvoiceLine
                    {
                        InvoiceId = inv.InvoiceId,
                        LineNumber = lineNo++,
                        ItemListId = listId,
                        ItemNameSnapshot = itemNameSnapshot,
                        Description = CleanDesc(r.ItemName),
                        Qty = Math.Round((decimal)r.TotalQty, 4, MidpointRounding.AwayFromZero),
                        Rate = Math.Round((decimal)r.UnitPrice, 4, MidpointRounding.AwayFromZero),
                        Amount = Math.Round((decimal)r.TotalAmount, MONEY2, MidpointRounding.AwayFromZero),
                        SourceType = r.Category ?? "",
                        SourceId = r.SourceInventoryId?.ToString(),
                        IsTaxable = true,
                        TechnicianName = CleanTech(r.TechnicianName)
                    });
                }

                // ✅ Add one tax line using the HstListId from the view
                if (needTax && tax != 0m)
                {
                    var hstListId = rows.FirstOrDefault()?.HstListId; // from view
                    pending.Add(new Models.InvoiceLine
                    {
                        InvoiceId = inv.InvoiceId,
                        LineNumber = pending.Count + 1,
                        ItemListId = hstListId,
                        ItemNameSnapshot = cfg.HstItem,
                        Description = "HST",
                        Qty = 0m,
                        Rate = 0m,
                        Amount = tax,
                        SourceType = "Tax",
                        SourceId = sheetId.ToString(),
                        IsTaxable = false,
                        TechnicianName = "N/A"
                    });
                }

                _context.InvoiceLines.AddRange(pending);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return new BuildInvoiceResult
                {
                    InvoiceId = inv.InvoiceId,
                    RefNumber = refNumber,
                    Status = "Ready",
                    ErrorMessage = null
                };
            }
            catch (Exception outer)
            {
                return new BuildInvoiceResult { Status = "Error", ErrorMessage = outer.GetBaseException().Message };
            }
        }




        public async Task<(string Status, string? Error, string? QbTxnID, DateTime? LastAttemptAt)>
            GetStatusAsync(int sheetId, CancellationToken ct = default)
        {
            var wo = await _context.WorkOrderSheets.AsNoTracking()
                         .FirstOrDefaultAsync(w => w.SheetId == sheetId, ct);
            if (wo == null) return ("NotFound", "Work order not found.", null, null);

            // WorkOrderNumber is non-nullable; fall back to sheetId if it's 0/unspecified
            var woNumber = (wo.WorkOrderNumber != 0) ? wo.WorkOrderNumber : sheetId;
            var refNumber = $"WO-{woNumber}";

            var inv = await _context.Invoices.AsNoTracking()
                         .FirstOrDefaultAsync(i => i.RefNumber == refNumber, ct);

            return inv == null
                ? ("None", null, null, null)
                : (inv.Status ?? "Ready", inv.ErrorMessage, inv.QbTxnId, inv.LastExportAttemptAt);
        }


        // ---------- helpers ----------
        private static string CleanDesc(string? s)
            => string.IsNullOrWhiteSpace(s) ? "" : s.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ").Trim();

        private static byte[] ComputeSourceHash(IReadOnlyList<Models.VwInvoicePreviewWithQbinv> rows)
        {
            var sb = new StringBuilder();
            foreach (var r in rows.OrderBy(x => x.Category).ThenBy(x => x.TechnicianName).ThenBy(x => x.ItemName))
                sb.Append($"{r.Category}|{r.TechnicianName}|{r.ItemName}|{(decimal)r.UnitPrice:0.####}|{(decimal)r.TotalQty:0.####}|{(decimal)r.TotalAmount:0.##};");
            return SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        }
    }
}

