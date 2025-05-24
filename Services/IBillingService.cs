using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IBillingService    
    {
        //Task<Vehicle> GetVehicleById(int id);
        //Task<Vehicle> GetVehicleBySamsaraId(string samsaraID);

        //Task<List<Vehicle>> GetAllVehicles();

        //Task<DTOs.PartsUsed> GetlabourById(int partID);

        Task<DTOs.Billing> GetLabourForWorkorder(int sheetID);
        Task<bool> UpdateBillingInfo(Billing billingDto);
        Task<bool> InsertBillingInformationAsync(Billing billingDto);
        //Task<List<Tech>> GetAllParts();

    }
}
