using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IWorkOrderFeeService    
    {


        //Task<DTOs.TravelLog> GetTravelByID(int mileageTimeID);
        Task<bool> DeleteWorkOrderFees(int technicianWorkOrderId);
        Task<bool> InsertWorkOrderFee(WorkOrderFee workOrderFee);
    }
}
