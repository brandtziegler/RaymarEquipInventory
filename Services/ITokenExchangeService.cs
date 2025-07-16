using Google.Apis.Drive.v3;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public interface ITokenExchangeService
    {
        Task<string> GetGoogleAccessTokenAsync();
        Task<DriveService> GetDriveServiceAsync();
    }
}