using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface ITechnicianService    
    {


        Task<Tech> GetTechByID(int techID); // Declares the async task method

        Task<List<Tech>> GetTechsByWorkOrder(int workOrderID);

        Task<List<Tech>> GetAllTechs();

    }
}
