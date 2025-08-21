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

        public MailService(IHttpClientFactory httpClientFactory, ILogger<MailService> log, IConfiguration cfg)
        {
            _httpClientFactory = httpClientFactory;
            _log = log;
            _resendKey = Environment.GetEnvironmentVariable("RESEND_KEY") ?? throw new InvalidOperationException("Resend API key missing.");
        }



        public async Task<MailBatchResult> SendWorkOrderEmailsAsync(
            WorkOrdMailContentBatch dto, CancellationToken ct = default)
        {
            // recipients already merged/validated in controller
            var recipients = dto.EmailAddresses?.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                            ?? new List<string>();
            var result = new MailBatchResult { Attempted = recipients.Count };

            if (recipients.Count == 0) return result;

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _resendKey);

            // choose first recipient as "to", rest as "bcc" (privacy, one call)
            var to = new[] { recipients[0] };
            var bcc = recipients.Skip(1).ToArray();

            var email = new
            {
                from = "service@taskfuel.app",
                to,              // string[] supported by Resend
                                 // cc = new[] { ... },         // optional
                bcc = bcc.Length > 0 ? bcc : null,
                subject = $"Work Order #{dto.WorkOrderNumber} for {dto.CustPath} Uploaded",
                html = $@"
            <h2>Work Order Synced</h2>
            <p><strong>Customer Path:</strong> {dto.CustPath}</p>
            <p><strong>Description:</strong> {dto.WorkDescription}</p>
            <p><strong>Work Order #{dto.WorkOrderNumber}</strong> is now live in Google Drive &amp; Azure SQL.</p>
            <p>You can view the uploaded files at this address:<br>
              <a href='https://drive.google.com/drive/folders/{dto.WorkOrderFolderId}'>
                View WO#{dto.WorkOrderNumber} Files on Google Drive for {dto.CustPath}
              </a>
            </p>
            <p><em>Need access? Use the Raymar Google account already shared with this folder.</em></p>
            <p><em>If you're not sure of the password, call me directly.</em></p>"
            };

            try
            {
                var resp = await client.PostAsJsonAsync("https://api.resend.com/emails", email, ct);
                if (resp.IsSuccessStatusCode)
                {
                    result.Succeeded = recipients.Count;      // everyone got the same message
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

