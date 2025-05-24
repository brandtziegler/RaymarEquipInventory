using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface ITechWOService    
    {
       Task<bool> InsertTechWOAsync(TechnicianWorkOrder techWODto);
    }
}
