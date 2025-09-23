

using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IQBItemCatalogImportService
    {
        Task<int> BulkInsertServiceItemsAsync(Guid runId, IEnumerable<CatalogItemDto> items, CancellationToken ct = default);
    }
}