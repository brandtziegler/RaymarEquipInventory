using Google.Apis.Drive.v3;
using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IDriveAuthService
    {

        Task<DriveService> GetDriveServiceFromUserTokenAsync();
        string GetConfigValue(string key);

    }
}
