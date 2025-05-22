using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IHourlyLabourService    
    {

        Task<List<DTOs.HourlyLabourType>> GetAllHourlyLabourTypes();
        Task<DTOs.HourlyLabourType> GetHourlyLabourById(int labourID);

        Task<bool> InsertRegularLabourAsync(RegularLabourLine labour);
    }
}
