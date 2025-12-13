using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.EntityFrameworkCore;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Helpers;
using RaymarEquipmentInventory.Models;
using Serilog;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

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



        public async Task<LoginResultDto> UpsertAuthUserAsync(
            string email,
            int personId,
            bool isActive,
            bool resetPassword = false,
            string? password = null)
        {
            var normalizedEmail = (email ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
                throw new ArgumentException("Email is required.", nameof(email));
            if (personId <= 0)
                throw new ArgumentException("PersonID is required.", nameof(personId));

            // Prefer link by PersonId, fallback to email match (helps if email changes later)
            var user = await _context.AuthUsers
                .FirstOrDefaultAsync(u => u.PersonId == personId);

            if (user == null)
            {
                user = await _context.AuthUsers
                    .FirstOrDefaultAsync(u => u.Email == normalizedEmail);
            }

            var isNew = (user == null);
            if (isNew)
            {
                user = new AuthUser
                {
                    CreatedAt = DateTime.UtcNow,
                    Uuid = Guid.NewGuid().ToString()
                };
                _context.AuthUsers.Add(user);
            }

            user.Email = normalizedEmail;
            user.PersonId = personId;
            user.IsActive = isActive;

            // Only set password on INSERT, or when explicitly resetting
            if (isNew || resetPassword)
            {
                var plain = string.IsNullOrWhiteSpace(password) ? "Raymar@1234" : password;


                // Salt can be anything reasonably unique/random; keep it simple for v1
                var salt = Guid.NewGuid().ToString("N");

                using var sha = SHA512.Create();
                var combinedBytes = Encoding.Unicode.GetBytes(plain + salt);
                var computedHashBytes = sha.ComputeHash(combinedBytes);
                var computedHash = BitConverter.ToString(computedHashBytes)
                    .Replace("-", "")
                    .ToUpperInvariant();

                user.Salt = salt;
                user.PasswordHash = computedHash;
            }

            await _context.SaveChangesAsync();

            // DON’T return hash/salt to RN in real life — but since your DTO includes it:
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
                Message = isNew ? "Auth user created." : "Auth user updated."
            };
        }

        private static string ComputeSha512HexUpper(string password, string salt)
        {
            using var sha = SHA512.Create();
            var bytes = Encoding.Unicode.GetBytes(password + salt); // matches your VerifyLoginAsync :contentReference[oaicite:1]{index=1}
            var hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        }

        private static string GenerateSaltBase64(int byteCount)
        {
            var bytes = RandomNumberGenerator.GetBytes(byteCount);
            return Convert.ToBase64String(bytes);
        }


        public async Task<LoginResultDto> ResetPasswordAsync(string email, string currentPassword, string newPassword)
        {
            var normalizedEmail = (email ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail)) throw new ArgumentException("Email required.");
            if (string.IsNullOrWhiteSpace(currentPassword)) throw new ArgumentException("Current password required.");
            if (string.IsNullOrWhiteSpace(newPassword)) throw new ArgumentException("New password required.");

            var user = await _context.AuthUsers.FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.IsActive == true);
            if (user == null) return new LoginResultDto { Success = false, Message = "Invalid email or password." };

            // verify current password (same logic as VerifyLoginAsync) :contentReference[oaicite:3]{index=3}
            var storedSalt = user.Salt ?? "";
            var storedHash = user.PasswordHash ?? "";

            using var sha = System.Security.Cryptography.SHA512.Create();
            var currentBytes = Encoding.Unicode.GetBytes(currentPassword + storedSalt);
            var currentHash = BitConverter.ToString(sha.ComputeHash(currentBytes)).Replace("-", "").ToUpperInvariant();

            if (!string.Equals(storedHash, currentHash, StringComparison.OrdinalIgnoreCase))
                return new LoginResultDto { Success = false, Message = "Invalid email or password." };

            // set new password
            var newSalt = Guid.NewGuid().ToString("N");
            var newBytes = Encoding.Unicode.GetBytes(newPassword + newSalt);
            var newHash = BitConverter.ToString(sha.ComputeHash(newBytes)).Replace("-", "").ToUpperInvariant();

            user.Salt = newSalt;
            user.PasswordHash = newHash;
            await _context.SaveChangesAsync();

            return new LoginResultDto { Success = true, Email = user.Email, PersonID = user.PersonId ?? 0, Message = "Password updated." };
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



