using RaymarEquipmentInventory.DTOs;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public interface IQBItemOtherImportService
    {
        /// Bulk copy NonInventory / OtherCharge / SalesTaxItem / SalesTaxGroup rows into dbo.QBItemOther_Staging.
        Task<int> BulkInsertOtherItemsAsync(Guid runId, IEnumerable<CatalogItemDto> items, bool firstPage = false, CancellationToken ct = default);
    }
}