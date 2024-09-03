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

            // Schedule job to run every 5 minutes, starting at 1:15 PM and ending at 4:00 PM EST
            _recurringJobManager.AddOrUpdate<IInventoryService>(
                "QuickBooksInventoryUpdateJob",
                service => service.GetInventoryPartsFromQuickBooksAsync(true),
                "15-59/5 13,14,15 * * *", // Cron expression: every 5 minutes from 1:15 PM to 3:59 PM
                jobOptions);
        }
    }
}

