using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IWorkOrderFeeService    
    {


        //Task<DTOs.TravelLog> GetTravelByID(int mileageTimeID);
        Task<bool> DeleteWorkOrderFees(int technicianWorkOrderId, CancellationToken cancellationToken = default);
        Task<bool> InsertWorkOrderFee(WorkOrderFee workOrderFee, CancellationToken cancellationToken = default);

        Task<int> InsertWorkOrderFeeBulkAsync(
    IEnumerable<DTOs.WorkOrderFee> items,
    CancellationToken cancellationToken = default);
    }
}
