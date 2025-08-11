using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using System;
using RaymarEquipmentInventory.Services;

namespace RaymarEquipmentInventory.BackgroundTasks
{
    public class HangfireConfiguration
    {
        private readonly IRecurringJobManager _recurringJobManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;
        private readonly IDriveAuthService _driveAuthService;
        private readonly IDriveUploaderService _driveUploaderService;
        public HangfireConfiguration(IRecurringJobManager recurringJobManager, IServiceProvider serviceProvider, ILogger logger, IDriveAuthService driveAuthService, IDriveUploaderService driveUploaderService)
        {
            _recurringJobManager = recurringJobManager;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _driveAuthService = driveAuthService;
            _driveUploaderService = driveUploaderService;
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
                "05 15 * * *",
                jobOptions);

            // ---- Example (left from before) ----
            // _recurringJobManager.AddOrUpdate<IInventoryService>(
            //     "QuickBooksInventoryUpdateJob",
            //     service => service.GetInventoryPartsFromQuickBooksAsync(true),
            //     "30 18 * * 1-5",
            //     jobOptions);
        }
    }
}

