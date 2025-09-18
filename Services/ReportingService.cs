using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using Serilog;
using ClosedXML.Excel;
using System.Globalization;
using CsvHelper;
using RaymarEquipmentInventory.Helpers;
using DocumentFormat.OpenXml.Spreadsheet;

namespace RaymarEquipmentInventory.Services
{
    /// <summary>
    /// Reporting façade (first report = Invoice Preview). No Document Intelligence here.
    /// Reuses your CSV and email patterns conceptually from DriveUploaderService. 
    /// </summary>
    public class ReportingService : IReportingService
    {
        // Match your existing DI style (keep symmetry with other services)
        private readonly RaymarInventoryDBContext _context;
        private readonly System.Net.Http.IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public ReportingService(
            RaymarInventoryDBContext context,
            System.Net.Http.IHttpClientFactory httpClientFactory,
            IConfiguration config)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }




        public Task<DateTimeOffset> GetWatermarkAsync(CancellationToken ct)
        => Task.FromResult(DateTimeOffset.UtcNow);

        public async Task<InventoryChangesResponse> GetChangesAsync(
           DateTimeOffset since, DateTimeOffset upto, int limit, CancellationToken ct)
        {
            limit = Math.Clamp(limit, 1, 1000);

            var rows = await _context.InventoryData
                .Where(x => x.LastUpdated != null
                         && x.LastUpdated > since.UtcDateTime
                         && x.LastUpdated <= upto.UtcDateTime)
                .OrderBy(x => x.LastUpdated)
                .ThenBy(x => x.InventoryId)
                .Take(limit)
                .Select(x => new InventoryChangeDto
                {
                    InventoryId = x.InventoryId.ToString(),
                    ItemName = x.ItemName ?? "",
                    ManufacturerPartNumber = x.ManufacturerPartNumber ?? "",
                    QuickBooksInvId = x.QuickBooksInvId ?? "",
                    Description = x.Description ?? "",
                    IsActive = x.IsActive ?? true,            // ← fix #1
                    LastUpdatedUtc = x.LastUpdated!.Value
                })
                .ToListAsync(ct);

            var hasMore = rows.Count() == limit;              // ← fix #2 (or use the 2-line property version)

            return new InventoryChangesResponse
            {
                Cursor = upto.ToString("O"),
                HasMore = hasMore,
                Upserts = rows
            };
        }

        /// <remarks>
        /// EF Core Power Tools will generate entities for your views:
        ///   - vw_InvoicePreview        (detail)
        ///   - vw_InvoicePreviewSummed  (summed)
        /// Map them to InvoiceCSVRow in a simple projection here.
        /// </remarks>
        public async Task<IReadOnlyList<InvoiceCSVRow>> GetInvoiceRowsAsync(int sheetId, bool summed, CancellationToken ct = default)
        {
            try
            {
                if (summed)
                {
                
                    return await _context.VwInvoicePreviewSummeds
                        .Where(v => v.SheetId == sheetId)
                        .Select(v => new InvoiceCSVRow
                        {
                            Category = v.Category,
                            SheetID = v.SheetId ?? 0,
                            TechnicianID = v.TechnicianId,
                            TechnicianName = v.TechnicianName,
                            ItemName = v.ItemName,
                            UnitPrice = Math.Round(v.UnitPrice ?? 0, 2, MidpointRounding.AwayFromZero),
                            TotalQty = Math.Round(v.TotalQty ?? 0, 2, MidpointRounding.AwayFromZero),
                            TotalAmount = Math.Round(v.TotalAmount ?? 0, 2, MidpointRounding.AwayFromZero)
                        })
                        .ToListAsync(ct);
                }
                else
                {
                    return await _context.VwInvoicePreviews
                        .Where(v => v.SheetId == sheetId)
                        .Select(v => new InvoiceCSVRow
                        {
                            Category = v.Category,
                            SheetID = v.SheetId ?? 0,
                            TechnicianID = v.TechnicianId,
                            TechnicianName = v.TechnicianName,
                            ItemName = v.ItemName,
                            UnitPrice = Math.Round(v.UnitPrice ?? 0, 2, MidpointRounding.AwayFromZero),
                            TotalQty = Math.Round(v.TotalQty ?? 0, 2, MidpointRounding.AwayFromZero),
                            TotalAmount = Math.Round(v.TotalAmount ?? 0, 2, MidpointRounding.AwayFromZero)
                        })
                        .ToListAsync(ct);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Failed to get invoice rows for SheetID {SheetId}, summed={Summed}", sheetId, summed);
                return new List<InvoiceCSVRow>();
            }
        }

        private static string CleanDesc(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            // IIF fields are tab-delimited; remove embedded tabs/CR/LF; keep quotes as literal characters.
            return s.Replace('\t', ' ')
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();
        }

        private static string CustomerFullNameFromCustPath(string? custPath)
        {
            if (string.IsNullOrWhiteSpace(custPath)) return "";
            // Replace " > " hierarchy with ":" which is how QBDT represents Full Name paths
            return string.Join(":", custPath.Split('>')
                                         .Select(x => x.Trim())
                                         .Where(x => x.Length > 0));
        }

        private static string AsEstDateString(DateTime utcNow)
        {
            // Windows TZ id (use "America/Toronto" on Linux if needed)
            var est = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, est);
            return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public async Task<CustomerImportResult> ImportCustomersAsync(Stream xlsx, CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var prevTimeout = _context.Database.GetCommandTimeout();
            _context.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

            try
            {
                // 1) Parse XLSX → DTOs (header validation inside)
                List<CustomerCsvRow> rows;
                try
                {
                    rows = CustomerImportHelper.ParseCustomerRows(xlsx).ToList();
                    if (rows.Count == 0)
                        throw new InvalidDataException("No data rows found beneath the header in CustomerUpsert.xlsx.");
                }
                catch (InvalidDataException ide)
                {
                    Serilog.Log.Warning(ide, "Customer import rejected: {Reason}", ide.Message);
                    throw;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Failed to read/parse the uploaded XLSX for customer import.");
                    throw new InvalidDataException(
                        "Could not read the uploaded XLSX. Ensure it is a valid .xlsx with expected columns (Full Name, Name, Company Name, Bill To Address, Main Phone, Main Email, Is Active).",
                        ex);
                }

                // 2) Transactional upsert (no inactivation sweep for customers)
                await using var tx = await _context.Database.BeginTransactionAsync(ct);

                CustomerImportResult upserts;

                try
                {
                    upserts = await CustomerImportHelper.ApplyUpsertsAsync(_context, rows, ct);

                    // enqueue hierarchy work for all touched rows (simple approach: any row whose FullName is in the file)
                    // You can narrow this to only inserted/updated in the helper if you prefer to track IDs there.
                    var idsToQueue = await _context.Customers
                        .Where(c => rows.Select(r => r.FullName).Contains(c.FullName))
                        .Select(c => c.CustomerId)
                        .ToListAsync(ct);

                    foreach (var cid in idsToQueue.Distinct())
                    {
                        // upsert a work item (idempotent)
                        await _context.Database.ExecuteSqlInterpolatedAsync($@"
IF NOT EXISTS (SELECT 1 FROM dbo.CustomerHierarchyWork WHERE StartCustomerID = {cid})
    INSERT dbo.CustomerHierarchyWork(StartCustomerID, RequestedAt) VALUES({cid}, SYSUTCDATETIME());
", ct);
                    }

                    await tx.CommitAsync(ct);
                    await _context.Database.ExecuteSqlRawAsync("EXEC dbo.Customer_ProcessHierarchyWork", ct);
                }
                catch (OperationCanceledException oce)
                {
                    try { await tx.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                    Serilog.Log.Warning(oce, "Customer import was cancelled.");
                    throw;
                }
                catch (DbUpdateException dbx)
                {
                    try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
                    Serilog.Log.Error(dbx, "Database update failed during customer import.");
                    throw new InvalidOperationException("Database update failed while applying the customer import.", dbx);
                }
                catch (Exception ex)
                {
                    try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
                    Serilog.Log.Error(ex, "Unexpected error during customer import transaction.");
                    throw;
                }

                sw.Stop();
                Serilog.Log.Information(
                    "Customer import complete: inserted={Inserted}, updated={Updated}, reparented={Reparented}, rejected={Rejected}, elapsedMs={Elapsed}",
                    upserts.Inserted, upserts.Updated, upserts.Reparented, upserts.Rejected, sw.ElapsedMilliseconds);

                return upserts;
            }
            finally
            {
                _context.Database.SetCommandTimeout(prevTimeout);
                if (sw.IsRunning) sw.Stop();
            }
        }


        public async Task<PartImportResult> ImportPartsAsync(Stream xlsx, CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // raise timeout only for this operation
            var prevTimeout = _context.Database.GetCommandTimeout();
            _context.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

            try
            {
                // 1) Parse XLSX → DTOs (header validation inside)
                List<PartCSVRow> rows;
                try
                {
                    rows = PartImportHelper.ParsePartRows(xlsx).ToList();
                    if (rows.Count == 0)
                        throw new InvalidDataException("No data rows found beneath the header in PartUpload.xlsx.");
                }
                catch (InvalidDataException ide)
                {
                    Serilog.Log.Warning(ide, "Parts import rejected: {Reason}", ide.Message);
                    throw;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Failed to read/parse the uploaded XLSX for parts import.");
                    throw new InvalidDataException(
                        "Could not read the uploaded XLSX. Ensure it is a valid .xlsx with the expected columns (Item, Description, Preferred Vendor, U/M, Price).",
                        ex);
                }

                // Build the active key set (normalized) once
                var activeKeys = rows
                    .Select(r => (r.Item ?? string.Empty).Trim())
                    .Where(k => k.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 2–3) Upserts + MarkInactive in one transaction for atomicity
                await using var tx = await _context.Database.BeginTransactionAsync(ct);

                PartImportResult upserts;
                int inactiveCount;
                List<PartChangeSample> inactiveSamples;

                try
                {
                    upserts = await PartImportHelper.ApplyUpsertsAsync(_context, rows, ct);

                    (inactiveCount, inactiveSamples) =
                        await PartImportHelper.MarkInactiveAsync(_context, activeKeys, ct);

                    await tx.CommitAsync(ct);
                }
                catch (OperationCanceledException oce)
                {
                    try { await tx.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                    Serilog.Log.Warning(oce, "Parts import was cancelled.");
                    throw;
                }
                catch (DbUpdateException dbx)
                {
                    try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
                    Serilog.Log.Error(dbx, "Database update failed during parts import.");
                    throw new InvalidOperationException("Database update failed while applying the parts import.", dbx);
                }
                catch (Exception ex)
                {
                    try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
                    Serilog.Log.Error(ex, "Unexpected error during parts import transaction.");
                    throw;
                }

                // 4) Merge counts + sample proofs
                var result = PartImportHelper.BuildAuditResult(upserts, inactiveCount, inactiveSamples);

                sw.Stop();
                Serilog.Log.Information(
                    "Parts import complete: inserted={Inserted}, updated={Updated}, reactivated={Reactivated}, inactivated={Inactivated}, rejected={Rejected}, elapsedMs={Elapsed}",
                    result.Inserted, result.Updated, result.Reactivated, result.MarkedInactive, result.Rejected, sw.ElapsedMilliseconds);

                return result;
            }
            finally
            {
                _context.Database.SetCommandTimeout(prevTimeout);
                if (sw.IsRunning) sw.Stop();
            }
        }



        public async Task<PartImportResult> ImportPartsAsync(byte[] xlsxBytes, CancellationToken ct = default)
        {
            using var ms = new MemoryStream(xlsxBytes);
            return await ImportPartsAsync(ms, ct); // calls your existing Stream-based method
        }

        public Task<PartImportResult> PreviewPartsImportAsync(Stream xlsx, CancellationToken ct = default)
        {
            // Parse only; do not touch DB
            var rows = PartImportHelper.ParsePartRows(xlsx).ToList();

            // Super-light estimate: treat all keys that exist as “updates/reactivations”
            // and all others as “inserts”. If you want this, say the word and I’ll wire it properly.
            return Task.FromResult(new PartImportResult
            {
                Inserted = 0,
                Updated = 0,
                Reactivated = 0,
                MarkedInactive = 0,
                Rejected = 0
            });
        }

        public (string FileName, MemoryStream Csv) GetPartsCsvPackage(Stream xlsx)
        {
            var rows = PartImportHelper.ParsePartRows(xlsx).ToList();

            var ms = new MemoryStream();
            using (var writer = new StreamWriter(ms, leaveOpen: true))
            {
                writer.WriteLine("Item,Description,U/M,Price");
                foreach (var r in rows)
                    writer.WriteLine($"{r.Item},{r.Description},{r.Uom},{r.Price:0.00}");
                writer.Flush();
            }
            ms.Position = 0;
            var name = $"Parts_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return (name, ms);
        }

        /// <summary>
        /// IIF LOGIC GOES BELOW
        /// </summary>
        /// <param name="sheetId"></param>
        /// <param name="summed"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        // Map a line to a QuickBooks Item (INVITEM) + Description according to our rules
        private (string InvItem, string Desc) MapLineToItem(IIFConfig cfg, InvoiceCSVRow row)
        {
            string item = cfg.LabourItem;              // default
            string desc = row.ItemName ?? "";

            switch ((row.Category ?? "").Trim())
            {
                case "RegularLabour":
                    {
                        // e.g. "Labour OT - Regular Labour"
                        var parts = (row.ItemName ?? "")
                                    .Split(" - ", 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length == 2)
                        {
                            var before = parts[0];   // "Labour" or "Labour OT"
                            var after = parts[1];   // "Regular Labour"
                            item = before.Equals("Labour OT", StringComparison.OrdinalIgnoreCase)
                                   ? cfg.LabourOtItem
                                   : cfg.LabourItem;
                            desc = after;
                        }
                        else
                        {
                            var isOt = (row.ItemName ?? "").Contains("OT", StringComparison.OrdinalIgnoreCase);
                            item = isOt ? cfg.LabourOtItem : cfg.LabourItem;
                            desc = row.ItemName ?? "";
                        }
                        break;
                    }

                case "MileageAndTravel":
                    {
                        // If the view’s ItemName is literally "Mileage", use Mileage item; otherwise use Travel Time
                        var isMileage = (row.ItemName ?? "").Equals("Mileage", StringComparison.OrdinalIgnoreCase);
                        item = isMileage ? cfg.MileageItem : cfg.TravelTimeItem;
                        desc = row.ItemName ?? "";
                        break;
                    }

                case "PartsUsed":
                    {
                        item = cfg.MiscPartItem;                // always post parts to the generic “Misc”
                        desc = row.ItemName ?? "";
                        break;
                    }

                case "WorkOrderFees":
                    {
                        item = cfg.FeeItem;                     // generic fee item; put the fee text in DESC
                        desc = row.ItemName ?? "";
                        break;
                    }

                default:
                    {
                        item = cfg.FeeItem;                     // fail-safe
                        desc = row.ItemName ?? "";
                        break;
                    }
            }

            return (item, CleanDesc(desc));
        }

        // ------------------------ BUILD IIF (one invoice) ------------------------

        public async Task<MemoryStream> BuildInvoiceIifAsync(int sheetId, bool summed, CancellationToken ct = default)
        {
            var cfg = IIFConfigHelper.GetIifConfig();

            // Header data
            var header = await _context.BillingInformations.AsNoTracking()
                .FirstOrDefaultAsync(b => b.SheetId == sheetId, ct);

            var wo = await _context.WorkOrderSheets.AsNoTracking()
                .FirstOrDefaultAsync(w => w.SheetId == sheetId, ct);

            var rows = await GetInvoiceRowsAsync(sheetId, summed, ct);

            string custFullName = CustomerFullNameFromCustPath(header?.CustPath);
            int workOrderNo = wo?.WorkOrderNumber ?? sheetId;
            string invDate = AsEstDateString(DateTime.UtcNow);

            // Totals (sum of per-line tax rounded to 2dp)
            decimal subtotal = 0m, totalTax = 0m;
            foreach (var r in rows)
            {
                subtotal += r.TotalAmount;
                var lineTax = Math.Round(r.TotalAmount * cfg.HstRate, 2, MidpointRounding.AwayFromZero);
                totalTax += lineTax;
            }
            decimal grand = subtotal + totalTax;

            // IIF writer (tab-delimited, CRLF, Windows-1252, no BOM)
            var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, Encoding.GetEncoding(1252), leaveOpen: true);

            // Headers (one time per file)
            writer.WriteLine("!TRNS\tTRNSTYPE\tDATE\tACCNT\tNAME\tCLASS\tAMOUNT\tDOCNUM\tTERMS\tPONUM\tMEMO");
            writer.WriteLine("!SPL\tSPLTYPE\tDATE\tACCNT\tNAME\tCLASS\tAMOUNT\tQNTY\tPRICE\tINVITEM\tTAXABLE\tDESC");
            writer.WriteLine("!ENDTRNS");

            // TRNS (invoice header) – AMOUNT is grand total
            writer.Write("TRNS"); writer.Write('\t');
            writer.Write("INVOICE"); writer.Write('\t');
            writer.Write(invDate); writer.Write('\t');
            writer.Write(cfg.ArAccount); writer.Write('\t');                 // ACCNT (A/R)
            writer.Write(custFullName); writer.Write('\t');                  // NAME (Customer Full Name)
            writer.Write('\t');                                              // CLASS
            writer.Write(grand.ToString(CultureInfo.InvariantCulture)); writer.Write('\t'); // AMOUNT (grand)
            writer.Write(workOrderNo.ToString(CultureInfo.InvariantCulture)); writer.Write('\t');    // DOCNUM
            writer.Write('\t');                                              // TERMS
            writer.Write((header?.Pono ?? "").Trim()); writer.Write('\t');   // PONUM
            writer.Write(CleanDesc(header?.WorkDescription));                // MEMO
            writer.WriteLine();

            // SPL lines – QTY + PRICE (AMOUNT blank so QB computes)
            foreach (var r in rows.OrderBy(x => x.Category).ThenBy(x => x.TechnicianName).ThenBy(x => x.ItemName))
            {
                var (invItem, desc) = MapLineToItem(cfg, r);

                writer.Write("SPL"); writer.Write('\t');
                writer.Write("INVOICE"); writer.Write('\t');
                writer.Write(invDate); writer.Write('\t');
                writer.Write('\t');                                       // ACCNT (item’s income account)
                writer.Write('\t');                                       // NAME
                writer.Write('\t');                                       // CLASS
                writer.Write('\t');                                       // AMOUNT (blank)
                writer.Write(r.TotalQty.ToString(CultureInfo.InvariantCulture)); writer.Write('\t');   // QNTY
                writer.Write(r.UnitPrice.ToString(CultureInfo.InvariantCulture)); writer.Write('\t');  // PRICE
                writer.Write(invItem); writer.Write('\t');                // INVITEM
                writer.Write("Y"); writer.Write('\t');                    // TAXABLE (we also add explicit tax SPL)
                writer.Write(desc);                                       // DESC
                writer.WriteLine();
            }

            // Sales-tax SPL – lock totals to your XLSX
            if (totalTax != 0m)
            {
                writer.Write("SPL"); writer.Write('\t');
                writer.Write("INVOICE"); writer.Write('\t');
                writer.Write(invDate); writer.Write('\t');
                writer.Write('\t');                                       // ACCNT
                writer.Write('\t');                                       // NAME
                writer.Write('\t');                                       // CLASS
                writer.Write(totalTax.ToString(CultureInfo.InvariantCulture)); writer.Write('\t'); // AMOUNT (explicit)
                writer.Write('\t');                                       // QNTY
                writer.Write('\t');                                       // PRICE
                writer.Write(cfg.HstItem); writer.Write('\t');            // INVITEM (Sales Tax Item)
                writer.Write('\t');                                       // TAXABLE
                writer.Write("HST");                                      // DESC
                writer.WriteLine();
            }

            // ENDTRNS
            writer.WriteLine("ENDTRNS");

            writer.Flush();
            ms.Position = 0;
            return ms;
        }

        public async Task<(string FileName, MemoryStream Iif)> GetInvoiceIifPackageAsync(
            int sheetId, bool summed, CancellationToken ct = default)
        {
            var wo = await _context.WorkOrderSheets.AsNoTracking()
                .FirstOrDefaultAsync(w => w.SheetId == sheetId, ct);

            var woNumber = wo?.WorkOrderNumber ?? sheetId;
            var name = $"Invoice_{woNumber}_{AsEstDateString(DateTime.UtcNow).Replace("-", "")}.iif";

            var iif = await BuildInvoiceIifAsync(sheetId, summed, ct);
            return (name, iif);
        }

        public async Task SendInvoiceIIFAsync(int sheetId, bool summed, CancellationToken ct = default)
        {
            var (fileName, iif) = await GetInvoiceIifPackageAsync(sheetId, summed, ct);

            var apiKey = Environment.GetEnvironmentVariable("Resend_Key");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Resend_Key not set.");

            string? to = Environment.GetEnvironmentVariable("Invoice_Receiver1");
            if (string.IsNullOrWhiteSpace(to))
                throw new InvalidOperationException("Invoice_Receiver1 not set.");

            var bccList = new List<string>();
            for (int i = 2; i <= 10; i++)
            {
                var addr = Environment.GetEnvironmentVariable($"Invoice_Receiver{i}");
                if (!string.IsNullOrWhiteSpace(addr)) bccList.Add(addr);
            }

            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            byte[] bytes = iif.ToArray();
            string b64 = Convert.ToBase64String(bytes);

            var payload = new
            {
                from = "service@taskfuel.app",
                to = new[] { to },
                bcc = bccList.Count > 0 ? bccList : null,
                subject = $"Invoice (IIF) for Work Order {sheetId}",
                text = $"Attached is the IIF for work order SheetID {sheetId}.",
                attachments = new[]
                {
            new { filename = fileName, content = b64 }
        }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync("https://api.resend.com/emails", content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Resend failed: {resp.StatusCode} {body}");

            Log.Information("Invoice IIF {File} sent to {To} (bcc={BccCount})", fileName, to, bccList.Count);
        }


        /// <summary>
        /// XLSX LOGIC GOES BELOW
        /// </summary>
        /// <param name="sheetId"></param>
        /// <param name="summed"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task<MemoryStream> BuildInvoiceXlsxAsync(
            int sheetId, bool summed, CancellationToken ct = default)
        {
            const decimal HstRate = 0.13m;

            // Header bits
            var header = await _context.BillingInformations
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.SheetId == sheetId, ct);

            var wo = await _context.WorkOrderSheets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.SheetId == sheetId, ct);

            string custPath = header?.CustPath ?? "";
            int workOrderNumber = wo?.WorkOrderNumber ?? sheetId; // fallback to SheetID if WO# is unknown
            string invDate = AsEstDateString(DateTime.UtcNow);

            // Rows
            var rows = await GetInvoiceRowsAsync(sheetId, summed, ct);

            // Subtotal + tax (sum of rounded line taxes)
            decimal subtotal = 0m;
            decimal totalTax = 0m;
            foreach (var r in rows)
            {
                subtotal += r.TotalAmount;
                var lineTax = Math.Round(r.TotalAmount * HstRate, 2, MidpointRounding.AwayFromZero);
                totalTax += lineTax;
            }
            decimal grand = subtotal + totalTax;

            // Build XLSX
            var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Invoice");

            int rIx = 1;

            // Header band
            ws.Cell(rIx, 1).Value = "CUSTOMER:";
            ws.Cell(rIx, 2).Value = custPath;
            ws.Cell(rIx, 5).Value = "WORK ORDER #:" + workOrderNumber;
            ws.Cell(rIx, 7).Value = "DATE:";
            ws.Cell(rIx, 8).Value = invDate;
            ws.Range(rIx, 1, rIx, 8).Style.Font.SetBold(); rIx += 2;

            // Column headers (exact labels you want)
            ws.Cell(rIx, 1).Value = "CHARGE CATEGORY";
            ws.Cell(rIx, 2).Value = "TECHNICIAN NAME";
            ws.Cell(rIx, 3).Value = "CHARGE";
            ws.Cell(rIx, 4).Value = "UNIT PRICE";
            ws.Cell(rIx, 5).Value = "QTY (KM/HOURS/JOBS)";
            ws.Cell(rIx, 6).Value = "TOTAL PRICE";
            ws.Range(rIx, 1, rIx, 6).Style.Font.SetBold();
            ws.Range(rIx, 1, rIx, 6).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F2F2F2"));
            rIx++;

            // Data rows (sorted like you want)
            foreach (var row in rows
                .OrderBy(x => x.Category)
                .ThenBy(x => x.TechnicianName)
                .ThenBy(x => x.ItemName))
            {
                ws.Cell(rIx, 1).Value = row.Category;
                ws.Cell(rIx, 2).Value = row.TechnicianName ?? "";
                ws.Cell(rIx, 3).Value = row.ItemName;
                ws.Cell(rIx, 4).Value = row.UnitPrice;
                ws.Cell(rIx, 5).Value = row.TotalQty;
                ws.Cell(rIx, 6).Value = row.TotalAmount;
                rIx++;
            }

            // Formatting
            ws.Column(1).Width = 20; // Category
            ws.Column(2).Width = 24; // Technician
            ws.Column(3).Width = 45; // Charge
            ws.Column(4).Width = 14; // Unit Price
            ws.Column(5).Width = 20; // Qty
            ws.Column(6).Width = 16; // Total

            ws.Column(4).Style.NumberFormat.Format = "$#,##0.00";
            ws.Column(6).Style.NumberFormat.Format = "$#,##0.00";
            ws.Column(5).Style.NumberFormat.Format = "0.00";

            ws.Range(3, 1, rIx - 1, 6).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(3, 1, rIx - 1, 6).Style.Border.InsideBorder = XLBorderStyleValues.Hair;

            // Totals section
            rIx += 1;
            ws.Cell(rIx, 5).Value = "SUB TOTAL";
            ws.Cell(rIx, 6).Value = subtotal; ws.Cell(rIx, 6).Style.NumberFormat.Format = "$#,##0.00";
            ws.Cell(rIx, 5).Style.Font.SetBold(); ws.Cell(rIx, 6).Style.Font.SetBold();
            rIx++;

            ws.Cell(rIx, 5).Value = $"HST ({(HstRate * 100m):0.#}%)";
            ws.Cell(rIx, 6).Value = totalTax; ws.Cell(rIx, 6).Style.NumberFormat.Format = "$#,##0.00";
            ws.Cell(rIx, 5).Style.Font.SetBold(); ws.Cell(rIx, 6).Style.Font.SetBold();
            rIx++;

            ws.Cell(rIx, 5).Value = "GRAND TOTAL";
            ws.Cell(rIx, 6).Value = grand; ws.Cell(rIx, 6).Style.NumberFormat.Format = "$#,##0.00";
            ws.Cell(rIx, 5).Style.Font.SetBold(); ws.Cell(rIx, 6).Style.Font.SetBold();

            // Return stream
            var ms = new MemoryStream();
            wb.SaveAs(ms);
            ms.Position = 0;
            return ms;
        }

        public async Task<(string FileName, MemoryStream Xlsx)> GetInvoiceXlsxPackageAsync(
            int sheetId, bool summed, CancellationToken ct = default)
        {
            // Pull WO# for filename
            var wo = await _context.WorkOrderSheets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.SheetId == sheetId, ct);

            var woNumber = wo?.WorkOrderNumber ?? sheetId;
            var name = $"Invoice_{woNumber}_{AsEstDateString(DateTime.UtcNow).Replace("-", "")}.xlsx";

            var xlsx = await BuildInvoiceXlsxAsync(sheetId, summed, ct);
            return (name, xlsx);
        }

        public async Task SendInvoiceXlsxAsync(int sheetId, bool summed, CancellationToken ct = default)
        {
            var (fileName, xlsx) = await GetInvoiceXlsxPackageAsync(sheetId, summed, ct);

            var apiKey = Environment.GetEnvironmentVariable("Resend_Key");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Resend_Key not set.");

            // To + BCC discovery
            string? to = Environment.GetEnvironmentVariable("Invoice_Receiver1");
            if (string.IsNullOrWhiteSpace(to))
                throw new InvalidOperationException("Invoice_Receiver1 not set.");

            var bccList = new List<string>();
            for (int i = 2; i <= 10; i++)
            {
                var addr = Environment.GetEnvironmentVariable($"Invoice_Receiver{i}");
                if (!string.IsNullOrWhiteSpace(addr)) bccList.Add(addr);
            }

            // Base64 attachment
            byte[] bytes = xlsx.ToArray();
            string b64 = Convert.ToBase64String(bytes);

            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var payload = new
            {
                from = "service@taskfuel.app",
                to = new[] { to },
                bcc = bccList.Count > 0 ? bccList : null,
                subject = $"Invoice for Work Order",
                text = $"Attached is the invoice for work order SheetID {sheetId}.",
                attachments = new[]
                {
            new { filename = fileName, content = b64 }
        }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync("https://api.resend.com/emails", content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                Log.Error("Resend failed ({Status}): {Body}", resp.StatusCode, body);
                throw new InvalidOperationException($"Resend failed: {resp.StatusCode} {body}");
            }

            Log.Information("Invoice XLSX {File} sent to {To} (bcc={BccCount})", fileName, to, bccList.Count);
        }

    }
}
