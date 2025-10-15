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

        public async Task<bool> VerifyLoginAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return false;

            var user = await _context.AuthUsers.FirstOrDefaultAsync(u => u.Email == email && u.IsActive == true);
            if (user == null)
                return false;

            // Convert stored hash and salt (they may be hex or plain text)
            string storedSalt = user.Salt ?? string.Empty;
            string storedHash = user.PasswordHash ?? string.Empty;

            // Rehash input password with the same salt
            using var sha = System.Security.Cryptography.SHA512.Create();
            var combinedBytes = System.Text.Encoding.UTF8.GetBytes(password + storedSalt);
            var computedHashBytes = sha.ComputeHash(combinedBytes);
            var computedHash = BitConverter.ToString(computedHashBytes)
                                           .Replace("-", "")
                                           .ToUpperInvariant();

            // Compare with stored hash (case-insensitive)
            return string.Equals(storedHash, computedHash, StringComparison.OrdinalIgnoreCase);
        }

    }





}



