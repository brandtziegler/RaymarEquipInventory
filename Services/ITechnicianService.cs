using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface ITechnicianService    
    {
        //Task<Vehicle> GetVehicleById(int id);
        //Task<Vehicle> GetVehicleBySamsaraId(string samsaraID);

        //Task<List<Vehicle>> GetAllVehicles();

        Task<Tech> GetTechByID(Int32 techID); // Declares the async task method

        Task<List<Tech>> GetTechsByWorkOrder(Int32 workOrderID);

        Task<List<Tech>> GetAllTechs();

    }
}
