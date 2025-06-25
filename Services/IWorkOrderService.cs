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

        Task<DTOs.WorkOrderInsertResult> UpdateWorkOrderAsync(DTOs.WorkOrdSheet workOrdSheet);

        Task<bool> RemoveBillFromWorkOrder(int billID, int sheetID);
        Task<bool> AddLbrToWorkOrder(LabourLine labourDTO);
        Task<bool> RemoveLbrFromWorkOrder(int labourID);
        Task<List<WorkOrderCard>> GetWorkOrderCardsAsync(DateTime? dateUploadedStart,
            DateTime? dateUploadedEnd,
            DateTime? dateTimeCompletedStart,
            DateTime? dateTimeCompletedEnd,
            int technicianId,
            string workOrderStatus = "COMPLETE",
            int? customerId = null);


        Task UpdateWOStatus(int sheetId, string workOrderStatus, string deviceId);

        Task<List<DTOs.PartsUsed>> GetPartsUsed(int sheetID);
        Task<List<DTOs.HourlyLbrSummary>> GetLabourLines(int sheetID, int? technicianId = null, int? labourTypeId = null);
        Task<List<DTOs.WorkOrderFee>> GetFees(int sheetID);

        Task<List<DTOs.TravelLog>> GetMileage(int sheetID);

        Task<DTOs.WorkOrder> GetWorkOrder(int sheetID);

        Task<DTOs.BillingMin?> GetBillingMin(int sheetID);

        Task<List<DTOs.WorkOrderBriefDetails>> GetWorkOrderBriefDetails();
        //Task<List<Tech>> GetAllParts();
        Task LogFailedSync(int sheetId, string reason);
    }
}
