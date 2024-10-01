using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IVehicleService    
    {
        //Task<Vehicle> GetVehicleById(int id);
        //Task<Vehicle> GetVehicleBySamsaraId(string samsaraID);

        //Task<List<Vehicle>> GetAllVehicles();

        Task UpdateOrInsertVehiclesAsync(List<Vehicle> vehicleList); // Declares the async task method

        Task UpdateVehicleLog(Int32 SheetID, Int32 VehicleID); // Declares the async task method
    }
}
