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

        // ✅ SettingsPanel: get all recipients + their type code
        public async Task<List<SettingsEmailRecipientDto>> GetAllRecipientsAsync(CancellationToken ct = default)
        {
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

        // ✅ SettingsPanel: upsert one recipient
        public async Task<SettingsEmailRecipientDto> UpsertSettingsEmailRecipientAsync(
            SettingsEmailRecipientDto dto,
            string? createdBy = null,
            CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var email = (dto.EmailAddress ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email))
                throw new InvalidOperationException("EmailAddress is required.");

            if (dto.NotificationTypeId <= 0)
                throw new InvalidOperationException("NotificationTypeId is required.");

            // Validate notification type and get its Code
            var type = await _context.EmailNotificationTypes
                .AsNoTracking()
                .SingleOrDefaultAsync(t =>
                    t.Id == dto.NotificationTypeId
                    && t.IsActive == true
                    && (t.Code == WorkOrderNotificationCode || t.Code == InvoiceNotificationCode),
                    ct);

            if (type == null)
                throw new InvalidOperationException("Invalid or inactive NotificationTypeId.");

            // Find existing row
            EmailNotificationRecipient? entity = null;

            if (dto.Id > 0)
            {
                entity = await _context.EmailNotificationRecipients
                    .SingleOrDefaultAsync(r => r.Id == dto.Id, ct);
            }

            // If inserting (Id=0), treat (TypeId + Email) as natural key to avoid duplicates
            if (entity == null && dto.Id <= 0)
            {
                entity = await _context.EmailNotificationRecipients
                    .SingleOrDefaultAsync(r =>
                        r.NotificationTypeId == dto.NotificationTypeId
                        && (r.EmailAddress ?? "").Trim().ToLower() == email,
                        ct);
            }

            var isInsert = (entity == null);

            if (isInsert)
            {
                entity = new EmailNotificationRecipient
                {
                    NotificationTypeId = dto.NotificationTypeId,
                    EmailAddress = email,
                    DisplayName = (dto.DisplayName ?? "").Trim(),
                    IsActive = dto.IsActive,
                    IsDefault = dto.IsDefault,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "SettingsPanel" : createdBy.Trim()
                };

                _context.EmailNotificationRecipients.Add(entity);
            }
            else
            {
                // Update
                entity!.NotificationTypeId = dto.NotificationTypeId; // allow type change if you want
                entity.EmailAddress = email;
                entity.DisplayName = (dto.DisplayName ?? "").Trim();
                entity.IsActive = dto.IsActive;
                entity.IsDefault = dto.IsDefault;
            }

            // Enforce single default per NotificationTypeId
            if (dto.IsDefault)
            {
                var others = await _context.EmailNotificationRecipients
                    .Where(r =>
                        r.NotificationTypeId == dto.NotificationTypeId
                        && r.Id != entity!.Id
                        && r.IsDefault == true)
                    .ToListAsync(ct);

                foreach (var o in others)
                    o.IsDefault = false;
            }

            await _context.SaveChangesAsync(ct);

            // Return canonical DTO (with notification code)
            return new SettingsEmailRecipientDto
            {
                Id = entity!.Id,
                NotificationTypeId = entity.NotificationTypeId,
                NotificationCode = type.Code,
                EmailAddress = (entity.EmailAddress ?? "").Trim().ToLowerInvariant(),
                DisplayName = (entity.DisplayName ?? "").Trim(),
                IsActive = entity.IsActive ?? false,
                IsDefault = entity.IsDefault
            };
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
            var extraList = (extraEmails ?? Enumerable.Empty<string>())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var type = await _context.EmailNotificationTypes
               .AsNoTracking()
               .SingleOrDefaultAsync(t => t.Code == code && t.IsActive == true, ct);

            if (type == null)
            {
                _log.LogWarning("No EmailNotificationType found for code {Code}. Using extras only.", code);
                return extraList;
            }

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
