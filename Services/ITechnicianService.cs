using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface ITechnicianService    
    {


        Task<Tech> GetTechByID(int techID); // Declares the async task method

        Task<List<Tech>> GetTechsByWorkOrder(int sheetID);

        Task<List<Tech>> GetAllTechs();
        Task<List<SettingsPersonDto>> GetSettingsPeople();

        Task<SettingsPersonDto> UpsertSettingsPerson(SettingsPersonDto dto);

    }
}
