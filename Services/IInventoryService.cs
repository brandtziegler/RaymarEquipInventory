using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IInventoryService
    {
        string GetProductById(int id);
        string GetAllProducts();

        Task<List<InventoryData>> GetInventoryPartsFromQuickBooksAsync(bool doUpdate = false); 
        Task UpdateOrInsertInventoryAsync(List<InventoryData> inventoryDataList); // Declares the async task method
    }
}
