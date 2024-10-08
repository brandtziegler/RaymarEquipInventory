using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.Services
{
    public interface IWorkOrderService    
    {
        //Task<Vehicle> GetVehicleById(int id);
        //Task<Vehicle> GetVehicleBySamsaraId(string samsaraID);

        //Task<List<Vehicle>> GetAllVehicles();

        //Task<DTOs.PartsUsed> GetlabourById(int partID);

        Task<DTOs.Billing> GetLabourForWorkorder(int sheetID);


        Task<bool> AddTechToWorkOrder(int techID, int sheetID);
        Task<bool> LaunchWorkOrder(BillingInformation billingInfo);
        //Task<List<Tech>> GetAllParts();

    }
}
