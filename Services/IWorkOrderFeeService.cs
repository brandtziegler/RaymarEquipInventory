using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IWorkOrderFeeService    
    {


        //Task<DTOs.TravelLog> GetTravelByID(int mileageTimeID);

        Task<bool> InsertWorkOrderFee(WorkOrderFee workOrderFee);
    }
}
