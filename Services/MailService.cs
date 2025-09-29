using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;
using System.Data;
using System.Reflection.PortableExecutable;
using RaymarEquipmentInventory.Helpers;
using Serilog;
using System.Net.Http.Headers;

namespace RaymarEquipmentInventory.Services
{
    public class MailService : IMailService
    {


      
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MailService> _log;
        private readonly string _resendKey;
        private readonly RaymarInventoryDBContext _context;

        public MailService(IHttpClientFactory httpClientFactory, ILogger<MailService> log, IConfiguration cfg, RaymarInventoryDBContext context)
        {
            _httpClientFactory = httpClientFactory;
            _log = log;
            _resendKey = Environment.GetEnvironmentVariable("RESEND_KEY") ?? throw new InvalidOperationException("Resend API key missing.");
            _context = context;
        }



        public async Task<MailBatchResult> SendWorkOrderEmailsAsync(
            WorkOrdMailContentBatch dto, CancellationToken ct = default)
        {
            // recipients already merged/validated in controller
            var recipients = dto.EmailAddresses?.Where(e => !string.IsNullOrWhiteSpace(e))
                               .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                           ?? new List<string>();
            var result = new MailBatchResult { Attempted = recipients.Count };
            if (recipients.Count == 0) return result;

            // ---------- helpers ----------
            static string H(string? s) =>
                System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

            static string Money(decimal v) =>
                v.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-CA"));

            static string Num(decimal v, int dp = 4) =>
                v.ToString($"N{dp}", System.Globalization.CultureInfo.GetCultureInfo("en-CA"));

            // ---------- load invoice lines for this WO ----------
            var lines = await _context.VwInvoiceLineExports
                .Where(x => x.WorkOrderId == dto.SheetId)
                .OrderBy(x => x.InvoiceId)
                .ThenBy(x => x.ItemNameSnapshot)
                .ToListAsync(ct);

            // ---------- optional invoice table (gate) ----------
            string tableHtml = string.Empty;
            if (lines.Count > 0)
            {
                decimal subtotal = lines
                    .Where(l => !string.Equals(l.SourceType, "Tax", StringComparison.OrdinalIgnoreCase))
                    .Sum(l => l.Amount ?? 0m);

                decimal hst = lines
                    .Where(l => string.Equals(l.SourceType, "Tax", StringComparison.OrdinalIgnoreCase)
                             || (l.ItemNameSnapshot ?? "").StartsWith("HST", StringComparison.OrdinalIgnoreCase))
                    .Sum(l => l.Amount ?? 0m);

                decimal grand = subtotal + hst;

                var sb = new System.Text.StringBuilder();
                sb.Append(@"
<table role=""presentation"" cellpadding=""6"" cellspacing=""0"" border=""0"" style=""width:100%; border-collapse:collapse; font-family:Segoe UI, Arial, sans-serif; font-size:13px;"">
  <thead>
    <tr style=""background:#e6e6e9; border-top:1px solid #ccc; border-bottom:1px solid #ccc;"">
      <th align=""left"">Item List ID</th>
      <th align=""left"">Type</th>
      <th align=""left"">Technician</th>
      <th align=""left"">Item</th>
      <th align=""left"">Description</th>
      <th align=""right"">Qty</th>
      <th align=""right"">Rate</th>
      <th align=""right"">Amount</th>
    </tr>
  </thead>
  <tbody>");

                for (int i = 0; i < lines.Count; i++)
                {
                    var r = lines[i];

                    // skip tax lines (we show them in summary instead)
                    if (string.Equals(r.SourceType, "Tax", StringComparison.OrdinalIgnoreCase) ||
                        (r.ItemNameSnapshot ?? "").StartsWith("HST", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var zebra = (i % 2 == 0) ? "#f6f6f8" : "#ffffff";
                    sb.Append($@"
    <tr style=""background:{zebra}; border-bottom:1px solid #eee;"">
      <td>{H(r.ItemListId)}</td>
      <td>{H(r.SourceType)}</td>
      <td>{H(string.IsNullOrWhiteSpace(r.TechnicianName) ? "N/A" : r.TechnicianName)}</td>
      <td>{H(r.ItemNameSnapshot)}</td>
      <td>{H(r.Description)}</td>
      <td align=""right"">{Num(r.Qty ?? 0m, 3)}</td>
      <td align=""right"">{Money(r.Rate ?? 0m)}</td>
      <td align=""right"">{Money(r.Amount ?? 0m)}</td>
    </tr>");
                }


                // summary rows (same table)
                sb.Append($@"
    <tr style=""background:#f2f2f5; border-top:2px solid #ccc;"">
      <td colspan=""7"" align=""right""><strong>Subtotal</strong></td>
      <td align=""right""><strong>{Money(subtotal)}</strong></td>
    </tr>
    <tr style=""background:#ffffff;"">
      <td colspan=""7"" align=""right"">HST</td>
      <td align=""right"">{Money(hst)}</td>
    </tr>
    <tr style=""background:#e9e9ed;"">
      <td colspan=""7"" align=""right""><strong>Grand Total</strong></td>
      <td align=""right""><strong>{Money(grand)}</strong></td>
    </tr>
  </tbody>
</table>");

                tableHtml = sb.ToString();
            }
            else
            {
                _log.LogInformation("No invoice lines yet for WO {WO}; sending email without invoice table.", dto.SheetId);
            }

            // ---------- compose final email ----------
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _resendKey);

            var to = new[] { recipients[0] };
            var bcc = recipients.Skip(1).ToArray();

            var subject = $"Work Order #{dto.WorkOrderNumber} for {dto.CustPath} Uploaded";

            var html = $@"
<h2 style=""font-family:Segoe UI, Arial; margin:0 0 8px 0;"">Work Order Synced</h2>
<p style=""font-family:Segoe UI, Arial; margin:0 0 8px 0;""><strong>Customer:</strong> {H(dto.CustPath)}</p>
<p style=""font-family:Segoe UI, Arial; margin:0 0 16px 0;""><strong>Description:</strong> {H(dto.WorkDescription)}</p>
<p style=""font-family:Segoe UI, Arial; margin:0 0 16px 0;"">
  <strong>Work Order #{dto.WorkOrderNumber}</strong> is now live in Google Drive &amp; Azure SQL.
</p>
<p style=""font-family:Segoe UI, Arial; margin:0 0 16px 0;"">
  <a href='https://drive.google.com/drive/folders/{H(dto.WorkOrderFolderId)}'>View WO #{dto.WorkOrderNumber} Files</a>
</p>
{tableHtml}
<p style=""font-family:Segoe UI, Arial; color:#666; font-size:12px; margin-top:16px;"">
  Need access? Use the shared Raymar Google account, or contact support.
</p>";

            var email = new
            {
                from = "service@taskfuel.app",
                to,
                bcc = (bcc.Length > 0 ? bcc : null),
                subject,
                html
            };

            try
            {
                var resp = await client.PostAsJsonAsync("https://api.resend.com/emails", email, ct);
                if (resp.IsSuccessStatusCode)
                {
                    result.Succeeded = recipients.Count;
                    _log.LogInformation("✅ Email sent to {Count} recipient(s) for WO {WO}", recipients.Count, dto.WorkOrderNumber);
                }
                else
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    result.Failed.Add(("batch", body));
                    _log.LogWarning("❌ Email batch failed: {Body}", body);
                }
            }
            catch (Exception ex)
            {
                result.Failed.Add(("batch", ex.Message));
                _log.LogError(ex, "❌ Exception sending batch email");
            }

            return result;
        }






    }

}

