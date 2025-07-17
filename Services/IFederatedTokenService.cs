using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IFederatedTokenService
    {
        Task<string> GetGoogleAccessTokenAsync();
        Task<(bool success, string message, string token)> TestAzureTokenAsync();
        Task<(bool success, string message, string token)> TestAzureTokenTwoAsync();
    }
}