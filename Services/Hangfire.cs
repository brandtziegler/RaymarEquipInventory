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

        public HangfireConfiguration(IRecurringJobManager recurringJobManager, IServiceProvider serviceProvider)
        {
            _recurringJobManager = recurringJobManager;
            _serviceProvider = serviceProvider;
        }

        public void InitializeJobs()
        {
            var jobOptions = new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"), // Set the time zone to EST
            };

            // Schedule job to run Monday through Friday at 6:30 PM EST
            //_recurringJobManager.AddOrUpdate<IInventoryService>(
            //    "QuickBooksInventoryUpdateJob",
            //    service => service.GetInventoryPartsFromQuickBooksAsync(true),
            //    "30 18 * * 1-5", // Cron expression: every weekday (Mon-Fri) at 6:30 PM EST
            //    jobOptions);

        }
    }
}

