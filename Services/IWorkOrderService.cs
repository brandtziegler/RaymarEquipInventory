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

        Task<DTOs.WorkOrderInsertResult?> InsertWorkOrderAsync(DTOs.WorkOrdSheet workOrdSheet);

        Task<bool> RemoveBillFromWorkOrder(int billID, int sheetID);
        Task<bool> AddLbrToWorkOrder(LabourLine labourDTO);
        Task<bool> RemoveLbrFromWorkOrder(int labourID);
        Task<List<WorkOrderCard>> GetWorkOrderCardsAsync(DateTime? dateUploadedStart,
            DateTime? dateUploadedEnd,
            DateTime? dateTimeCompletedStart,
            DateTime? dateTimeCompletedEnd,
            string workOrderStatus = "COMPLETE",
            int? customerId = null);

        Task<List<DTOs.PartsUsed>> GetWorkOrderPartsUsed(int sheetID);

        Task<DTOs.WorkOrder> GetWorkOrder(int sheetID);
        Task<List<DTOs.WorkOrderBriefDetails>> GetWorkOrderBriefDetails();
        //Task<List<Tech>> GetAllParts();

    }
}
