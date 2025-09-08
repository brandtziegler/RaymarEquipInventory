using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using System;
using RaymarEquipmentInventory.Services;
using RaymarEquipmentInventory.BackgroundEmailTasks;

namespace RaymarEquipmentInventory.BackgroundTasks
{
    public class HangfireConfiguration
    {
        private readonly IRecurringJobManager _recurringJobManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;

        public HangfireConfiguration(IRecurringJobManager recurringJobManager)
        {
            _recurringJobManager = recurringJobManager;
        }

        public void InitializeJobs()
        {
            var jobOptions = new RecurringJobOptions
            {
                // Windows identifier; handles DST despite the name.
                // (If you ever run on Linux, swap to "America/Toronto".)
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"),
            };

            // ---- DATABASE BACKUP ----
            // Prod: every day at 11:59 PM ET
            _recurringJobManager.AddOrUpdate<IDriveUploaderService>(
                "Nightly-DB-Backup-23_59-ET",
                svc => svc.BackupDatabaseToGoogleDriveAsync(CancellationToken.None),
                "59 23 * * *",
                jobOptions);

            // Test: every day at 2:40 PM ET
            _recurringJobManager.AddOrUpdate<IDriveUploaderService>(
                "Daily-DB-Backup-Test-14_40-ET",
                svc => svc.BackupDatabaseToGoogleDriveAsync(CancellationToken.None),
                "59 15 * * *",
                jobOptions);


            _recurringJobManager.AddOrUpdate<PartsInboxJob>(
            "Inventory-Upsert-Import",
            j => j.RunAsync(CancellationToken.None),
            "*/5 * * * *",
            jobOptions);


            //_recurringJobManager.AddOrUpdate<CustomersInboxJob>(
            //    "Customers-Upsert-Import",
            //    j => j.RunAsync(CancellationToken.None),
            //    "*/5 * * * *",
            //    jobOptions);

            // ---- Example (left from before) ----
            // _recurringJobManager.AddOrUpdate<IInventoryService>(
            //     "QuickBooksInventoryUpdateJob",
            //     service => service.GetInventoryPartsFromQuickBooksAsync(true),
            //     "30 18 * * 1-5",
            //     jobOptions);
        }
    }
}

