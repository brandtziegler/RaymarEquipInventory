using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IPartService    
    {
        //Task<Vehicle> GetVehicleById(int id);
        //Task<Vehicle> GetVehicleBySamsaraId(string samsaraID);

        //Task<List<Vehicle>> GetAllVehicles();

        Task<DTOs.PartsUsed> GetPartByID(int partID);

        Task<List<DTOs.PartsUsed>> GetPartsByWorkOrder(int sheetID);

        //Task<List<Tech>> GetAllParts();

    }
}
