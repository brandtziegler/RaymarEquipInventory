namespace RaymarEquipmentInventory.Services
{
    public interface IInventoryService
    {
        string GetProductById(int id);
        string GetAllProducts();
    }
}
