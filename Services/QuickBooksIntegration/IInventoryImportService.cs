using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Data;                    // for SqlDbType
using System.Threading;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;

using RaymarEquipmentInventory.DTOs; // RaymarInventoryDBContext

namespace RaymarEquipmentInventory.Services
{
    public interface IInventoryImportService
    {
        Task<int> BulkInsertInventoryAsync(
            Guid runId,
            IEnumerable<InventoryItemDto> items,
            CancellationToken ct = default);

        Task<InventoryBackupSyncResult> SyncInventoryDataAsync(
            Guid runId,
            CancellationToken ct = default);
    }
}
