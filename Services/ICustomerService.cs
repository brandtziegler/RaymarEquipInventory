using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface ICustomerService
    {
        Task<List<CustomerData>> GetCustomersFromQuickBooksAsnyc(bool doUpdate = false);

    }
}
