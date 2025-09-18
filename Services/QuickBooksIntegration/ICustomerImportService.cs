using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface ICustomerImportService
    {
        Task<int> BulkInsertCustomersAsync(
            Guid runId,
            IEnumerable<CustomerData> customers,
            CancellationToken ct = default);

        Task<CustomerBackupSyncResult> SyncCustomerBackupAsync(
            Guid runId,
            CancellationToken ct = default);
    }

    public sealed class CustomerBackupSyncResult
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Deactivated { get; set; }
    }
}
