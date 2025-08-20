using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IHourlyLabourService    
    {

        Task<List<DTOs.HourlyLabourType>> GetAllHourlyLabourTypes();
        Task<DTOs.HourlyLabourType> GetHourlyLabourById(int labourID);
        Task<int> DeleteRegularLabourAsync(int sheetId, CancellationToken ct = default);
        Task<bool> InsertRegularLabourAsync(RegularLabourLine labour);
        Task<int> InsertRegularLabourBulkAsync(
            IEnumerable<RegularLabourLine> lines, CancellationToken ct = default);
    }
}
