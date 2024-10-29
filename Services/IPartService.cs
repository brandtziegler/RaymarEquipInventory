using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IPartService    
    {
        //Task<Vehicle> GetVehicleById(int id);
        //Task<Vehicle> GetVehicleBySamsaraId(string samsaraID);

        //Task<List<Vehicle>> GetAllVehicles();

        Task<DTOs.PartsUsed> GetPartByID(int partID);

        Task<List<DTOs.PartsUsed>> GetPartsByWorkOrder(int sheetID, int pageNumber, int pageSize, string itemName, int? qtyUsedMin, int? qtyUsedMax, string manufacturerPartNumber);

        Task<int> GetPartsCountByWorkOrder(int sheetID, string itemName, int? qtyUsedMin, int? qtyUsedMax, string manufacturerPartNumber);

        Task<bool> UpdatePart(DTOs.PartsUsed partsUsedDto);

        //Task<List<Tech>> GetAllParts();

    }
}
