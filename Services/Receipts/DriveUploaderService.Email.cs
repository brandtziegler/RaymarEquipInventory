using System;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Serilog;

namespace RaymarEquipmentInventory.Services
{
    public partial class DriveUploaderService
    {
        /// <summary>
        /// Sends the CSV via Resend to all env-configured recipients.
        /// Env vars must be named: Receipt_Receiver_1, Receipt_Receiver_2, ...
        /// </summary>
        public async Task SendCsvEmailViaResendAsync(
            string subject,
            string htmlBody,
            string attachmentName,
            Stream csvStream,
            CancellationToken ct = default)
        {
            // Discover recipients from environment
            var recipients = GetReceiptRecipientsFromEnvironment();
            if (recipients.Count == 0)
            {
                Log.Warning("📧 No recipients found in environment (Receipt_Receiver_*). Skipping email send.");
                return;
            }

            // Read CSV once to base64 (stream might be forward-only)
            using var ms = new MemoryStream();
            if (csvStream.CanSeek) csvStream.Position = 0;
            await csvStream.CopyToAsync(ms, ct);
            var base64 = Convert.ToBase64String(ms.ToArray());

            var resendKey = Environment.GetEnvironmentVariable("Resend_Key");
            if (string.IsNullOrWhiteSpace(resendKey))
                throw new InvalidOperationException("Resend API key missing (Resend_Key).");

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", resendKey);

            foreach (var to in recipients)
            {
                var emailPayload = new
                {
                    from = "service@taskfuel.app",
                    to = to,                 // send individually (privacy + clearer logging)
                    subject = subject,
                    html = htmlBody,
                    attachments = new[]
                    {
                        new { filename = attachmentName, content = base64 }
                    }
                };

                try
                {
                    var response = await client.PostAsJsonAsync("https://api.resend.com/emails", emailPayload, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct);
                        Log.Warning("❌ Resend failed to {Email}: {Status} {Body}", to, response.StatusCode, body);
                    }
                    else
                    {
                        Log.Information("✅ Resend email sent to {Email} ({Subject})", to, subject);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Error(ex, "❌ Resend threw for {Email}", to);
                }
            }
        }

        /// <summary>
        /// Reads environment variables named Receipt_Receiver_* and returns a de-duplicated, validated list of emails.
        /// Supports optional unnumbered key 'Receipt_Receiver' as well.
        /// </summary>
        private static IReadOnlyList<string> GetReceiptRecipientsFromEnvironment()
        {
            var env = Environment.GetEnvironmentVariables(); // IDictionary
            var items = new List<(int Order, string Email)>();
            var rxIndexed = new Regex(@"^Receipt_Receiver_(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            foreach (DictionaryEntry de in env)
            {
                if (de.Key is not string key || de.Value is not string raw) continue;
                if (!key.StartsWith("Receipt_Receiver", StringComparison.OrdinalIgnoreCase)) continue;

                var email = raw?.Trim();
                if (string.IsNullOrWhiteSpace(email)) continue;

                // Validate basic email format
                if (!IsValidEmail(email))
                {
                    Log.Warning("⚠️ Skipping invalid email in env {Key}: '{Value}'", key, raw);
                    continue;
                }

                // Determine order by numeric suffix when present
                var m = rxIndexed.Match(key);
                var order = m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : int.MaxValue;
                items.Add((order, email));
            }

            // Sort by explicit index, then by email, and de-duplicate case-insensitively
            var result = items
                .OrderBy(t => t.Order)
                .ThenBy(t => t.Email, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.Email)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (result.Count > 0)
                Log.Information("📧 Found {Count} receipt recipient(s): {Recipients}", result.Count, string.Join(", ", result));

            return result;
        }

        private static bool IsValidEmail(string email)
        {
            try { _ = new MailAddress(email); return true; }
            catch { return false; }
        }
    }
}
