using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IMileageAndTravelService    
    {


        Task<DTOs.TravelLog> GetTravelByID(int mileageTimeID);
        Task EnsureThreeSegmentsAsync(int sheetId);
        Task<bool> InsertTravelLogAsync(TravelLog travelLog);
    }
}
