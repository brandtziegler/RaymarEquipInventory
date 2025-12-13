using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
namespace RaymarEquipmentInventory.Services
{
    public class RecipientService : IRecipientService
    {
        private readonly RaymarInventoryDBContext _context;
        private readonly ILogger<RecipientService> _log;

        private const string WorkOrderNotificationCode = "WORK_ORDER";
        private const string InvoiceNotificationCode = "INVOICE";

        public RecipientService(
            RaymarInventoryDBContext context,
            ILogger<RecipientService> log)
        {
            _context = context;
            _log = log;
        }

        // ✅ SettingsPanel DTO (inline)


        // ✅ SettingsPanel: get all recipients + their type code
        public async Task<List<SettingsEmailRecipientDto>> GetAllRecipientsAsync(CancellationToken ct = default)
        {
            // Join recipients to types so client can render Type column reliably.
            var query =
                from r in _context.EmailNotificationRecipients.AsNoTracking()
                join t in _context.EmailNotificationTypes.AsNoTracking()
                    on r.NotificationTypeId equals t.Id
                where t.IsActive == true
                      && (t.Code == WorkOrderNotificationCode || t.Code == InvoiceNotificationCode)
                select new SettingsEmailRecipientDto
                {
                    Id = r.Id,
                    NotificationTypeId = r.NotificationTypeId,
                    NotificationCode = t.Code,
                    EmailAddress = (r.EmailAddress ?? "").Trim(),
                    DisplayName = (r.DisplayName ?? "").Trim(),
                    IsActive = (r.IsActive ?? false),
                    IsDefault = r.IsDefault,
                };

            return await query
                .OrderBy(x => x.NotificationTypeId)
                .ThenByDescending(x => x.IsDefault)
                .ThenBy(x => x.EmailAddress)
                .ToListAsync(ct);
        }

        public Task<IReadOnlyList<string>> GetWorkOrderRecipientsAsync(
            int sheetId,
            int workOrderNumber,
            IEnumerable<string>? extraEmails = null,
            CancellationToken ct = default)
        {
            return GetRecipientsByCodeAsync(
                WorkOrderNotificationCode,
                sheetId,
                workOrderNumber,
                extraEmails,
                ct);
        }

        public Task<IReadOnlyList<string>> GetInvoiceRecipientsAsync(
            int sheetId,
            int workOrderNumber,
            IEnumerable<string>? extraEmails = null,
            CancellationToken ct = default)
        {
            return GetRecipientsByCodeAsync(
                InvoiceNotificationCode,
                sheetId,
                workOrderNumber,
                extraEmails,
                ct);
        }

        private async Task<IReadOnlyList<string>> GetRecipientsByCodeAsync(
            string code,
            int sheetId,
            int workOrderNumber,
            IEnumerable<string>? extraEmails,
            CancellationToken ct)
        {
            // normalize extras
            var extraList = (extraEmails ?? Enumerable.Empty<string>())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // look up notification type
            var type = await _context.EmailNotificationTypes
               .AsNoTracking()
               .SingleOrDefaultAsync(
                   t => t.Code == code && t.IsActive == true,
                   ct);

            if (type == null)
            {
                _log.LogWarning(
                    "No EmailNotificationType found for code {Code}. Using extras only.",
                    code);
                return extraList;
            }

            // base recipients from settings
            var baseRecipients = await _context.EmailNotificationRecipients
                .AsNoTracking()
                .Where(r => r.NotificationTypeId == type.Id && (r.IsActive ?? false))
                .Select(r => r.EmailAddress)
                .ToListAsync(ct);

            var merged = baseRecipients
                .Concat(extraList)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (merged.Count == 0)
            {
                _log.LogWarning(
                    "No recipients resolved for {Code} {SheetId}/{WorkOrderNumber}.",
                    code, sheetId, workOrderNumber);
            }

            return merged;
        }
    }
}
