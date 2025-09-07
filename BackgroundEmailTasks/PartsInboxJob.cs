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
using RaymarEquipmentInventory.DTOs;


namespace RaymarEquipmentInventory.BackgroundEmailTasks
{
    public class PartsInboxJob
    {
        private readonly IReportingService _reporting;
        private readonly ILogger<PartsInboxJob> _log;
        private readonly PartsInboxOptions _opt;

        public PartsInboxJob(IReportingService reporting, IOptions<PartsInboxOptions> opt, ILogger<PartsInboxJob> log)
        {
            _reporting = reporting;
            _opt = opt.Value;
            _log = log;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            using var imap = new ImapClient();
            await imap.ConnectAsync(_opt.ImapHost, _opt.ImapPort, MailKit.Security.SecureSocketOptions.SslOnConnect, ct);
            await imap.AuthenticateAsync(_opt.Email, _opt.Password, ct);

            var inbox = imap.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

            var matches = await inbox.SearchAsync(
                SearchQuery.NotSeen.And(SearchQuery.SubjectContains(_opt.ExpectedSubject)), ct);

            foreach (var uid in matches)
            {
                var msg = await inbox.GetMessageAsync(uid, ct);
                var attachment = msg.Attachments
                    .OfType<MimePart>()
                    .FirstOrDefault(a => a.FileName.Contains(_opt.ExpectedAttachment, StringComparison.OrdinalIgnoreCase));

                if (attachment == null) continue;

                await using var ms = new MemoryStream();
                await attachment.Content.DecodeToAsync(ms, ct);
                ms.Position = 0;

                var result = await _reporting.ImportPartsAsync(ms, ct);
                _log.LogInformation("Processed {File}: Inserted={Ins}, Updated={Upd}, Rejected={Rej}",
                    attachment.FileName, result.Inserted, result.Updated, result.Rejected);

                await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
            }

            await imap.DisconnectAsync(true, ct);
        }
    }
}
