using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IMileageAndTravelService    
    {


        Task<DTOs.TravelLog> GetTravelByID(int mileageTimeID, CancellationToken ct = default);
        Task EnsureThreeSegmentsAsync(int sheetId, CancellationToken ct = default);
        Task<bool> InsertTravelLogAsync(TravelLog travelLog, CancellationToken ct = default);

        Task<bool> DeleteTravelLogAsync(int sheetId, CancellationToken ct = default);

        Task<int> InsertTravelLogBulkAsync(
    IEnumerable<TravelLog> entries,
    CancellationToken ct = default);

    }
}
