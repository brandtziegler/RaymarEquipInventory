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
            var result = new MailBatchResult { Attempted = dto.EmailAddresses.Count };
            if (dto.EmailAddresses.Count == 0) return result;

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _resendKey);

            // Resend: 2 req/sec -> ~500ms interval. Give cushion.
            var minInterval = TimeSpan.FromMilliseconds(600);
            var lastSend = DateTimeOffset.MinValue;

            var succeeded = 0;

            // Send SEQUENTIALLY with throttle + retry
            foreach (var to in dto.EmailAddresses)
            {
                // Throttle to provider limit
                var now = DateTimeOffset.UtcNow;
                var wait = lastSend + minInterval - now;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct);

                try
                {
                    // Build email (your exact content)
                    var email = new
                    {
                        from = "service@taskfuel.app",
                        to,
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

                    // Retry on 429/5xx with exponential backoff
                    var attempts = 0;
                    HttpResponseMessage? resp = null;

                    while (attempts < 3)
                    {
                        attempts++;
                        resp = await client.PostAsJsonAsync("https://api.resend.com/emails", email, ct);

                        if (resp.IsSuccessStatusCode) break;

                        // 429 handling – honor Retry-After when present
                        if ((int)resp.StatusCode == 429)
                        {
                            var retryAfter = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(800);
                            await Task.Delay(retryAfter, ct);
                        }
                        else if ((int)resp.StatusCode >= 500)
                        {
                            // backoff 700ms, 1400ms on retries
                            var backoff = TimeSpan.FromMilliseconds(700 * attempts);
                            await Task.Delay(backoff, ct);
                        }
                        else
                        {
                            break; // 4xx other than 429 – don’t spin
                        }
                    }

                    lastSend = DateTimeOffset.UtcNow;

                    if (resp is not null && resp.IsSuccessStatusCode)
                    {
                        succeeded++;
                        _log.LogInformation("✅ Email sent to {Email} for WO {WO}", to, dto.WorkOrderNumber);
                    }
                    else
                    {
                        var body = resp is null ? "no response" : await resp.Content.ReadAsStringAsync(ct);
                        lock (result.Failed) result.Failed.Add((to, body));
                        _log.LogWarning("❌ Email failed to {Email}: {Body}", to, body);
                    }
                }
                catch (Exception ex)
                {
                    lastSend = DateTimeOffset.UtcNow;
                    lock (result.Failed) result.Failed.Add((to, ex.Message));
                    _log.LogError(ex, "❌ Exception sending email to {Email}", to);
                }
            }

            result.Succeeded = succeeded;
            _log.LogInformation("📬 Mail batch complete: {Ok}/{Total}", result.Succeeded, result.Attempted);
            return result;
        }




    }

}

