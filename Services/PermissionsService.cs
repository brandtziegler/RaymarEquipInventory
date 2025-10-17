using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;
using System.Data;
using System.Reflection.PortableExecutable;
using RaymarEquipmentInventory.Helpers;
using Serilog;
using Microsoft.AspNetCore.Server.IISIntegration;
using System.Text;

namespace RaymarEquipmentInventory.Services
{
    public class PermissionsService : IPermissionsService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public PermissionsService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }


        public async Task<DTOs.RolesAndPermissions?> GetPermissionsByTechnicianIdAsync(int technicianId)
        {
            
            var result = await _context.VwRolesMins
                .Where(v => v.TechnicianId == technicianId)
                .Select(v => new DTOs.RolesAndPermissions
                {
                    TechnicianID = v.TechnicianId,
                    PersonID = v.PersonId,
                    RolePermissionID = v.RolePermissionId,
                    CanDownloadFromCloud = v.CanDownloadFromCloud,
                    CanUploadToCloud = v.CanUploadToCloud,
                    CanApprove = v.CanApprove,
                    CanAssignTech = v.CanAssignTech,
                    CanExportInvoice = v.CanExportInvoice
                })
                .FirstOrDefaultAsync();

            return result;
        }

        public async Task<LoginResultDto?> VerifyLoginAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return null;

            var user = await _context.AuthUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email && u.IsActive == true);

            if (user == null)
                return null;

            string storedSalt = user.Salt ?? string.Empty;
            string storedHash = user.PasswordHash ?? string.Empty;

            using var sha = System.Security.Cryptography.SHA512.Create();
            var combinedBytes = Encoding.Unicode.GetBytes(password + storedSalt);
            var computedHashBytes = sha.ComputeHash(combinedBytes);
            var computedHash = BitConverter.ToString(computedHashBytes)
                                           .Replace("-", "")
                                           .ToUpperInvariant();

            bool passwordMatch = string.Equals(storedHash, computedHash, StringComparison.OrdinalIgnoreCase);
            if (!passwordMatch)
                return new LoginResultDto { Success = false, Message = "Invalid email or password." };

            return new LoginResultDto
            {
                Success = true,
                UUID = user.Uuid,
                Email = user.Email,
                PersonID = user.PersonId ?? 0,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                IsActive = user.IsActive == true,
                Salt = user.Salt,
                PasswordHash = user.PasswordHash,
                Message = "Login successful."
            };
        }



    }





}



