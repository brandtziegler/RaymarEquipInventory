using System;
using System.IO;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace RaymarEquipmentInventory.Services
{
    public partial class DriveUploaderService
    {
        private async Task SendCsvEmailViaResendAsync(
            string toEmail,
            string subject,
            string htmlBody,
            string attachmentName,
            Stream csvStream,
            CancellationToken ct = default)
        {
            // read CSV to base64
            using var ms = new MemoryStream();
            await csvStream.CopyToAsync(ms, ct);
            var base64 = Convert.ToBase64String(ms.ToArray());

            var resendKey = Environment.GetEnvironmentVariable("Resend_Key");
            if (string.IsNullOrWhiteSpace(resendKey))
                throw new InvalidOperationException("Resend API key missing");

            var emailPayload = new
            {
                from = "service@taskfuel.app",
                to = toEmail,
                subject = subject,
                html = htmlBody,
                attachments = new[]
                {
                    new { filename = attachmentName, content = base64 }
                }
            };

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", resendKey);

            var response = await client.PostAsJsonAsync("https://api.resend.com/emails", emailPayload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                Log.Warning("❌ Resend failed: {Status} {Body}", response.StatusCode, body);
            }
            else
            {
                Log.Information("✅ Resend email sent to {Email} ({Subject})", toEmail, subject);
            }
        }
    }
}
