using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IPermissionsService    
    {
        Task<DTOs.RolesAndPermissions?> GetPermissionsByTechnicianIdAsync(int technicianId);
        Task<LoginResultDto?> VerifyLoginAsync(string email, string password);

        Task<LoginResultDto> UpsertAuthUserAsync(
            string email,
            int personId,
            bool isActive,
            bool resetPassword = false,
            string? password = null);

        Task<LoginResultDto> ResetPasswordAsync(string email, string currentPassword, string newPassword);
    }
}
