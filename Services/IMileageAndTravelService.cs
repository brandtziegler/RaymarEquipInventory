using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IMileageAndTravelService    
    {


        Task<DTOs.TravelLog> GetTravelByID(int mileageTimeID);

        Task<bool> InsertTravelLogAsync(TravelLog travelLog);
    }
}
