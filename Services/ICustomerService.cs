using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface ICustomerService
    {
        Task<List<CustomerData>> GetCustomersFromQuickBooksAsnyc(bool doUpdate = false);
        Task<List<CustomerData>> GetAllCustomers();
        Task<CustomerData> GetCustomerByID(int custID);
        Task<WatermarkResponse> GetWatermarkAsync(CancellationToken ct = default);
        Task<CustomerChangesResponse> GetCustomerChangesAsync(DateTime? sinceUtc, int limit = 500, CancellationToken ct = default);
        Task<List<CustomerData>> GetRecentChangedCustomers(byte[] sinceVersion, int limit = 1200);

    }
}
