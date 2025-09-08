using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaymarEquipmentInventory.Services;
using RaymarEquipmentInventory.DTOs;   // IReportingService

namespace RaymarEquipmentInventory.BackgroundEmailTasks
{
    public class CustomersInboxJob
    {
        private readonly IReportingService _reporting; // will add ImportCustomersAsync(...)
        private readonly ILogger<CustomersInboxJob> _log;
        private readonly CustomersInboxOptions _opt;

        public CustomersInboxJob(
            IReportingService reporting,
            IOptions<CustomersInboxOptions> opt,
            ILogger<CustomersInboxJob> log)
        {
            _reporting = reporting;
            _opt = opt.Value;
            _log = log;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            using var imap = new ImapClient();
            await imap.ConnectAsync(_opt.ImapHost, _opt.ImapPort, SecureSocketOptions.SslOnConnect, ct);
            await imap.AuthenticateAsync(_opt.Email, _opt.Password, ct);

            var inbox = imap.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

            // Find unseen messages with the expected subject
            var matches = await inbox.SearchAsync(
                SearchQuery.NotSeen.And(SearchQuery.SubjectContains(_opt.ExpectedSubject)), ct);

            foreach (var uid in matches)
            {
                var msg = await inbox.GetMessageAsync(uid, ct);

                // Find the XLSX attachment by name token
                var attachment = msg.Attachments
                    .OfType<MimePart>()
                    .FirstOrDefault(a => a.FileName != null &&
                                         a.FileName.Contains(_opt.ExpectedAttachment, StringComparison.OrdinalIgnoreCase));

                if (attachment == null)
                {
                    _log.LogDebug("Message {Subject} matched but no attachment containing token '{Token}' was found.",
                        msg.Subject, _opt.ExpectedAttachment);
                    continue;
                }

                await using var ms = new MemoryStream();
                await attachment.Content.DecodeToAsync(ms, ct);
                ms.Position = 0;

                // ⬇️ Will fail to compile until we add this method on IReportingService
                var result = await _reporting.ImportCustomersAsync(ms, ct);

                _log.LogInformation("Customer import from {File}: Inserted={Ins}, Updated={Upd}, Reparented={Rep}, Rejected={Rej}",
                    attachment.FileName, result.Inserted, result.Updated, result.Reparented, result.Rejected);

                await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
            }

            await imap.DisconnectAsync(true, ct);
        }
    }
}
