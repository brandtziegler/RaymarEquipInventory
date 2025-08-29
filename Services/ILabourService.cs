using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface ILabourService    
    {
        //Task<Vehicle> GetVehicleById(int id);
        //Task<Vehicle> GetVehicleBySamsaraId(string samsaraID);

        //Task<List<Vehicle>> GetAllVehicles();

        //Task<DTOs.PartsUsed> GetlabourById(int partID);

        Task<List<DTOs.LabourLine>> GetLabourByWorkOrder(int sheetID);

        Task<LabourLine> GetLabourById(int labourID);
        Task<bool> UpdateLabour(LabourLine labourDTO);
        //Task<List<Tech>> GetAllParts();
        Task<IReadOnlyList<DTOs.LabourType>> GetCatalogueAsync(
            bool onlyActive = true,
            CancellationToken ct = default);
    }
}
