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

            var succeeded = 0;                 // local counter for thread-safe increments
            var gate = new SemaphoreSlim(3);   // modest parallelism to be polite

            var tasks = dto.EmailAddresses.Select(async to =>
            {
                await gate.WaitAsync(ct);
                try
                {
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
                        View WO#{dto.WorkOrderNumber} Files on Google Drive for {dto.CustPath}>{dto.WorkOrderNumber}
                      </a>
                    </p>
                    <p><em>Need access? Use the Raymar Google account already shared with this folder.</em></p>
                    <p><em>If you're not sure of the password, call me directly.</em></p>"
                    };

                    var resp = await client.PostAsJsonAsync("https://api.resend.com/emails", email, ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        Interlocked.Increment(ref succeeded);
                        _log.LogInformation("✅ Email sent to {Email} for WO {WO}", to, dto.WorkOrderNumber);
                    }
                    else
                    {
                        var body = await resp.Content.ReadAsStringAsync(ct);
                        lock (result.Failed) result.Failed.Add((to, body));
                        _log.LogWarning("❌ Email failed to {Email}: {Body}", to, body);
                    }
                }
                catch (Exception ex)
                {
                    lock (result.Failed) result.Failed.Add((to, ex.Message));
                    _log.LogError(ex, "❌ Exception sending email to {Email}", to);
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();

            await Task.WhenAll(tasks);

            result.Succeeded = succeeded;
            _log.LogInformation("📬 Mail batch complete: {Ok}/{Total}", result.Succeeded, result.Attempted);
            return result;
        }





    }

}

