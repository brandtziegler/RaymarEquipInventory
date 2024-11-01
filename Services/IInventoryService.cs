using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IInventoryService
    {
        string GetProductById(int id);
        string GetAllProducts();

        Task<List<InventoryData>> GetInventoryPartsFromQuickBooksAsync(bool doUpdate = false);

        Task<List<InventoryForDropdown>> GetDropdownInfo(string searchTerm);

        Task<List<InventoryForDropdown>> GetAllInventoryItems();

        Task UpdateOrInsertInventoryAsync(List<InventoryData> inventoryDataList); // Declares the async task method

        Task<int> InsertInventoryAsync(string itemName, string itemDescription, string mfgPartNo); // Declares the async task method
    }
}
