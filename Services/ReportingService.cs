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

        private static string AsEstDateString(DateTime utcNow)
        {
            // Windows TZ id (use "America/Toronto" on Linux if needed)
            var est = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, est);
            return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

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

            var apiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("RESEND_API_KEY not set.");

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
                from = Environment.GetEnvironmentVariable("RESEND_FROM") ?? "invoices@yourdomain.com",
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
