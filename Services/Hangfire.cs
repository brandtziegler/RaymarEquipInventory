using Hangfire;
using System;
using System.Threading;
using RaymarEquipmentInventory.Services;
using RaymarEquipmentInventory.BackgroundEmailTasks;

namespace RaymarEquipmentInventory.BackgroundTasks
{
    public class HangfireConfiguration
    {
        private readonly IRecurringJobManager _recurringJobManager;

        public HangfireConfiguration(IRecurringJobManager recurringJobManager)
        {
            _recurringJobManager = recurringJobManager ?? throw new ArgumentNullException(nameof(recurringJobManager));
        }

        public void InitializeJobs()
        {
            var jobOptions = new RecurringJobOptions
            {
                // Windows identifier; handles DST. On Linux, use "America/Toronto".
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"),
            };

            // ---- DATABASE BACKUP (test schedule: 3:59 PM ET daily) ----
            _recurringJobManager.AddOrUpdate<IDriveUploaderService>(
                "Daily-DB-Backup-Test-15_59-ET",
                svc => svc.BackupDatabaseToGoogleDriveAsync(CancellationToken.None),
                "59 15 * * *",
                jobOptions);

            // ---- INVENTORY (parts) inbox import — every 5 minutes ----
            _recurringJobManager.AddOrUpdate<PartsInboxJob>(
                "Inventory-Upsert-Import",
                j => j.RunAsync(CancellationToken.None),
                "*/5 * * * *",
                jobOptions);

            // ---- CUSTOMERS inbox import — every 5 minutes ----
            _recurringJobManager.AddOrUpdate<CustomersInboxJob>(
                "Customers-Upsert-Import",
                j => j.RunAsync(CancellationToken.None),
                "*/5 * * * *",
                jobOptions);
        }
    }
}


