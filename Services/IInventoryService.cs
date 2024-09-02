using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IInventoryService
    {
        string GetProductById(int id);
        string GetAllProducts();

        List<InventoryData> GetInventoryPartsFromQuickBooks(); // <- Add this line
    }
}
