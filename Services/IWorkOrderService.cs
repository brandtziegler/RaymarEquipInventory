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
        Task<bool> DeleteTechFromWorkOrder(int techID, int sheetID);
        Task<bool> AddPartToWorkOrder(DTOs.PartsUsed partsUsedDto);
        Task<bool> RemovePartFromWorkOrder(int partUsedId, int sheetId);

        Task<bool> LaunchWorkOrder(Billing billingInfo);
        Task<bool> RemoveBillFromWorkOrder(int billID, int sheetID);
        Task<bool> AddLbrToWorkOrder(LabourLine labourDTO);

        //Task<List<Tech>> GetAllParts();

    }
}
