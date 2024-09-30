using RaymarEquipmentInventory.DTOs;


namespace RaymarEquipmentInventory.Services
{
    public interface ISamsaraApiService
    {
        Task<Vehicle> GetVehicleByID(string vehicleID = "");

        Task<List<Vehicle>> GetAllVehicles();
    }
}
