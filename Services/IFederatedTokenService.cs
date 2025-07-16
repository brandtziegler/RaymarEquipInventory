using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IFederatedTokenService
    {
        Task<string> GetGoogleAccessTokenAsync();
    }
}