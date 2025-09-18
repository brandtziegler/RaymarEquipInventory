using Microsoft.EntityFrameworkCore;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Helpers;
using RaymarEquipmentInventory.Models;
using System.Security.Cryptography;
using System.Text;

namespace RaymarEquipmentInventory.Services
{
    /// Builds an idempotent Invoice + InvoiceLines snapshot using vw_InvoicePreviewExport.
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
            // ---------- helpers ----------
            static string CleanDesc(string? s) =>
                string.IsNullOrWhiteSpace(s) ? "" : s.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ").Trim();

            BuildInvoiceResult Error(string msg)
            {
                Serilog.Log.Error("InvoiceSnapshot FAIL sheetId={SheetId}: {Msg}", sheetId, msg);
                return new BuildInvoiceResult { Status = "Error", ErrorMessage = msg };
            }

            try
            {
                // 1) Source rows
                List<Models.VwInvoicePreviewExport> rows;
                try
                {
                    rows = await _context.VwInvoicePreviewExports
                        .Where(v => v.SheetId == sheetId)
                        .AsNoTracking()
                        .ToListAsync(ct);
                }
                catch (Exception ex)
                {
                    return Error($"Failed reading vw_InvoicePreviewExport: {ex.GetBaseException().Message}");
                }

                if (rows.Count == 0)
                    return Error($"No invoice data found for SheetID {sheetId}.");

                // 2) WO + customer (from view)
                Models.WorkOrderSheet? wo;
                try
                {
                    wo = await _context.WorkOrderSheets.AsNoTracking()
                            .FirstOrDefaultAsync(w => w.SheetId == sheetId, ct);
                }
                catch (Exception ex)
                {
                    return Error($"Failed reading WorkOrderSheets: {ex.GetBaseException().Message}");
                }
                if (wo == null) return Error($"Work order not found for SheetID {sheetId}.");

                var any = rows[0];
                var customerListId = any.CustomerListId;
                var customerId = any.CustomerId;

                if (string.IsNullOrWhiteSpace(customerListId))
                    return Error("Customer ListID missing on export view.");

                // 3) RefNumber + date
                var woNumber = (wo.WorkOrderNumber != 0) ? wo.WorkOrderNumber : sheetId;
                var refNumber = $"WO-{woNumber}";
                DateTime invoiceDate;
                try
                {
                    invoiceDate = TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")).Date;
                }
                catch
                {
                    invoiceDate = DateTime.UtcNow.Date; // fallback if timezone not found
                }

                // 4) Config & which service items we’ll need ListIDs for
                IIFConfig cfg;
                try
                {
                    cfg = IIFConfigHelper.GetIifConfig();
                }
                catch (Exception ex)
                {
                    return Error($"Failed to load IIF config: {ex.GetBaseException().Message}");
                }

                var needNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in rows)
                {
                    if (string.Equals(r.Category, "PartsUsed", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(r.QuickBooksInvId))
                            needNames.Add(cfg.MiscPartItem);
                    }
                    else if (string.Equals(r.Category, "MileageAndTravel", StringComparison.OrdinalIgnoreCase))
                    {
                        needNames.Add((r.ItemName ?? "").Equals("Mileage", StringComparison.OrdinalIgnoreCase)
                                      ? cfg.MileageItem : cfg.TravelTimeItem);
                    }
                    else if (string.Equals(r.Category, "RegularLabour", StringComparison.OrdinalIgnoreCase))
                    {
                        var isOt = (r.ItemName ?? "").StartsWith("Labour OT", StringComparison.OrdinalIgnoreCase);
                        needNames.Add(isOt ? cfg.LabourOtItem : cfg.LabourItem);
                    }
                    else if (string.Equals(r.Category, "WorkOrderFees", StringComparison.OrdinalIgnoreCase))
                    {
                        needNames.Add(cfg.FeeItem);
                    }
                }

                // Totals + tax
                const int MONEY2 = 2;
                decimal subtotal = rows.Sum(r => Math.Round((decimal)r.TotalAmount, MONEY2, MidpointRounding.AwayFromZero));
                bool needTax = subtotal != 0m && cfg.HstRate != 0m;
                if (needTax) needNames.Add(cfg.HstItem);
                decimal tax = Math.Round(subtotal * cfg.HstRate, MONEY2, MidpointRounding.AwayFromZero);
                decimal total = subtotal + tax;

                // Try resolve service-item ListIDs (best effort)
                Dictionary<string, string> nameToListId;
                try
                {
                    var listIdMap = await _context.InventoryData
                        .Where(i => i.ItemName != null && needNames.Contains(i.ItemName))
                        .Select(i => new { i.ItemName, i.QuickBooksInvId })
                        .ToListAsync(ct);

                    nameToListId = listIdMap
                        .GroupBy(x => x.ItemName!, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Select(v => v.QuickBooksInvId).FirstOrDefault() ?? "", StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    return Error($"Failed resolving service ListIDs from InventoryData: {ex.GetBaseException().Message}");
                }

                var missingNames = needNames
                    .Where(n => !nameToListId.TryGetValue(n, out var lid) || string.IsNullOrWhiteSpace(lid))
                    .OrderBy(n => n)
                    .ToList();

                // 5) TX: upsert header, build/replace lines
                await using var tx = await _context.Database.BeginTransactionAsync(ct);

                Models.Invoice? existing;
                try
                {
                    existing = await _context.Invoices
                        .Include(h => h.InvoiceLines)
                        .FirstOrDefaultAsync(i => i.RefNumber == refNumber, ct);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    return Error($"Failed reading Invoice by RefNumber '{refNumber}': {ex.GetBaseException().Message}");
                }

                if (existing != null && string.Equals(existing.Status, "Exported", StringComparison.OrdinalIgnoreCase))
                    return new BuildInvoiceResult
                    {
                        InvoiceId = existing.InvoiceId,
                        RefNumber = existing.RefNumber,
                        Status = existing.Status,
                        ErrorMessage = "Invoice already exported; cannot rebuild."
                    };

                // Header fields that require extra reads
                string? poNum = null, memo = null;
                try
                {
                    poNum = await _context.BillingInformations.AsNoTracking()
                                .Where(b => b.SheetId == sheetId)
                                .Select(b => b.Pono)
                                .FirstOrDefaultAsync(ct);
                    memo = await _context.BillingInformations.AsNoTracking()
                                .Where(b => b.SheetId == sheetId)
                                .Select(b => b.WorkDescription)
                                .FirstOrDefaultAsync(ct);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    return Error($"Failed reading BillingInformation: {ex.GetBaseException().Message}");
                }

                // Create/update header
                Models.Invoice inv;
                try
                {
                    if (existing == null)
                    {
                        inv = new Models.Invoice
                        {
                            WorkOrderId = wo.SheetId,
                            CustomerId = customerId,
                            CustomerListId = customerListId,
                            RefNumber = refNumber,
                            TxnDate = invoiceDate,
                            DueDate = null,
                            TermsRef = null,
                            Ponumber = poNum,
                            Memo = memo,
                            Subtotal = subtotal,
                            TaxAmount = tax,
                            Total = total,
                            Currency = "CAD",
                            Status = "Ready",
                            ErrorMessage = missingNames.Count > 0
                                                    ? $"Missing QuickBooks ListIDs for: {string.Join(", ", missingNames)}"
                                                    : null,
                            SourceHash = ComputeSourceHash(rows),
                            QbTxnId = null,
                            QbEditSequence = null,
                            ExportAttemptCount = 0,
                            LastExportAttemptAt = null,
                            ExportedAt = null,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _context.Invoices.Add(inv);
                        await _context.SaveChangesAsync(ct); // need InvoiceId
                    }
                    else
                    {
                        inv = existing;
                        inv.CustomerId = customerId;
                        inv.CustomerListId = customerListId;
                        inv.TxnDate = invoiceDate;
                        inv.Ponumber = poNum;
                        inv.Memo = memo;
                        inv.Subtotal = subtotal;
                        inv.TaxAmount = tax;
                        inv.Total = total;
                        inv.Status = "Ready";
                        inv.ErrorMessage = missingNames.Count > 0
                                                ? $"Missing QuickBooks ListIDs for: {string.Join(", ", missingNames)}"
                                                : null;
                        inv.SourceHash = ComputeSourceHash(rows);
                        inv.UpdatedAt = DateTime.UtcNow;

                        _context.InvoiceLines.RemoveRange(inv.InvoiceLines);
                        await _context.SaveChangesAsync(ct);
                    }
                }
                catch (DbUpdateException dbx)
                {
                    await tx.RollbackAsync(ct);
                    var baseMsg = dbx.GetBaseException().Message;
                    // Handle unique RefNumber race
                    if (baseMsg.IndexOf("UX_Invoice_RefNumber", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        baseMsg.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var dupe = await _context.Invoices.AsNoTracking()
                                      .FirstOrDefaultAsync(i => i.RefNumber == refNumber, ct);
                        if (dupe != null)
                            return new BuildInvoiceResult { InvoiceId = dupe.InvoiceId, RefNumber = dupe.RefNumber, Status = dupe.Status };
                    }
                    return Error($"DB write failed (header): {baseMsg}");
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    return Error($"Unexpected error writing header: {ex.GetBaseException().Message}");
                }

                // Build lines in-memory for preflight diagnostics
                var pending = new List<Models.InvoiceLine>();
                try
                {
                    int lineNo = 1;
                    foreach (var r in rows
                        .OrderBy(x => x.Category)
                        .ThenBy(x => x.TechnicianName)
                        .ThenBy(x => x.ItemName))
                    {
                        string itemNameSnapshot;
                        string? listId;

                        if (string.Equals(r.Category, "PartsUsed", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrWhiteSpace(r.QuickBooksInvId))
                            {
                                itemNameSnapshot = r.ItemName ?? "Part";
                                listId = r.QuickBooksInvId!;
                            }
                            else
                            {
                                itemNameSnapshot = cfg.MiscPartItem;
                                listId = (nameToListId.TryGetValue(cfg.MiscPartItem, out var lid) && !string.IsNullOrWhiteSpace(lid)) ? lid : null;
                            }
                        }
                        else if (string.Equals(r.Category, "MileageAndTravel", StringComparison.OrdinalIgnoreCase))
                        {
                            var isMileage = (r.ItemName ?? "").Equals("Mileage", StringComparison.OrdinalIgnoreCase);
                            itemNameSnapshot = isMileage ? cfg.MileageItem : cfg.TravelTimeItem;
                            listId = (nameToListId.TryGetValue(itemNameSnapshot, out var lid) && !string.IsNullOrWhiteSpace(lid)) ? lid : null;
                        }
                        else if (string.Equals(r.Category, "RegularLabour", StringComparison.OrdinalIgnoreCase))
                        {
                            var isOt = (r.ItemName ?? "").StartsWith("Labour OT", StringComparison.OrdinalIgnoreCase);
                            itemNameSnapshot = isOt ? cfg.LabourOtItem : cfg.LabourItem;
                            listId = (nameToListId.TryGetValue(itemNameSnapshot, out var lid) && !string.IsNullOrWhiteSpace(lid)) ? lid : null;
                        }
                        else
                        {
                            itemNameSnapshot = cfg.FeeItem;
                            listId = (nameToListId.TryGetValue(itemNameSnapshot, out var lid) && !string.IsNullOrWhiteSpace(lid)) ? lid : null;
                        }

                        pending.Add(new Models.InvoiceLine
                        {
                            InvoiceId = inv.InvoiceId,
                            LineNumber = lineNo++,
                            ItemListId = listId,                      // may be NULL
                            ItemNameSnapshot = itemNameSnapshot,
                            Description = CleanDesc(r.ItemName),
                            Qty = Math.Round((decimal)r.TotalQty, 4, MidpointRounding.AwayFromZero),
                            Rate = Math.Round((decimal)r.UnitPrice, 4, MidpointRounding.AwayFromZero),
                            Amount = Math.Round((decimal)r.TotalAmount, MONEY2, MidpointRounding.AwayFromZero),
                            ClassRef = null,
                            ServiceDate = null,
                            TaxCodeRef = null,
                            SourceType = r.Category ?? "",
                            SourceId = r.SourceInventoryId?.ToString(),
                            Uom = null,
                            IsTaxable = true
                        });
                    }

                    if (needTax && tax != 0m)
                    {
                        string? taxListId = (nameToListId.TryGetValue(cfg.HstItem, out var lid) && !string.IsNullOrWhiteSpace(lid)) ? lid : null;

                        pending.Add(new Models.InvoiceLine
                        {
                            InvoiceId = inv.InvoiceId,
                            LineNumber = pending.Count + 1,
                            ItemListId = taxListId,                   // may be NULL
                            ItemNameSnapshot = cfg.HstItem,
                            Description = "HST",
                            Qty = 0m,
                            Rate = 0m,
                            Amount = tax,
                            ClassRef = null,
                            ServiceDate = null,
                            TaxCodeRef = null,
                            SourceType = "Tax",
                            SourceId = sheetId.ToString(),
                            Uom = null,
                            IsTaxable = false
                        });
                    }
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    return Error($"Unexpected error building lines in memory: {ex.GetBaseException().Message}");
                }

                // Pre-diagnose null ItemListIDs that might violate NOT NULL schema
                var nullItemListLines = pending
                    .Where(l => string.IsNullOrWhiteSpace(l.ItemListId))
                    .Select(l => $"{l.SourceType}:{l.ItemNameSnapshot}")
                    .Distinct()
                    .ToList();

                try
                {
                    _context.InvoiceLines.AddRange(pending);
                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch (DbUpdateException dbx)
                {
                    await tx.RollbackAsync(ct);
                    var baseMsg = dbx.GetBaseException().Message;

                    // Friendly diagnosis for the most likely constraint
                    if (baseMsg.IndexOf("ItemListID", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        baseMsg.IndexOf("NULL", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var hint =
                            "DB rejected NULL ItemListID on InvoiceLine. " +
                            "Either configure QuickBooks ListIDs for the following items, " +
                            "or allow NULLs with:\n" +
                            "ALTER TABLE dbo.InvoiceLine ALTER COLUMN ItemListID NVARCHAR(50) NULL;\n" +
                            $"Missing: {string.Join(", ", nullItemListLines)}";
                        return Error(hint);
                    }

                    // Unique indexes, FK issues, etc.
                    return Error($"DB write failed (lines): {baseMsg}");
                }
                catch (OperationCanceledException oce)
                {
                    try { await tx.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                    return Error($"Operation cancelled: {oce.Message}");
                }
                catch (Exception ex)
                {
                    try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
                    return Error($"Unexpected error writing lines: {ex.GetBaseException().Message}");
                }

                // Success
                return new BuildInvoiceResult
                {
                    InvoiceId = inv.InvoiceId,
                    RefNumber = inv.RefNumber,
                    Status = inv.Status,
                    ErrorMessage = inv.ErrorMessage // may contain missing names note
                };
            }
            catch (Exception outer)
            {
                return Error($"Unhandled failure: {outer.GetBaseException().Message}");
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

        private static byte[] ComputeSourceHash(IReadOnlyList<Models.VwInvoicePreviewExport> rows)
        {
            var sb = new StringBuilder();
            foreach (var r in rows.OrderBy(x => x.Category).ThenBy(x => x.TechnicianName).ThenBy(x => x.ItemName))
                sb.Append($"{r.Category}|{r.TechnicianName}|{r.ItemName}|{(decimal)r.UnitPrice:0.####}|{(decimal)r.TotalQty:0.####}|{(decimal)r.TotalAmount:0.##};");
            return SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        }
    }
}

