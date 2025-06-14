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
                    CanAssignTech = v.CanAssignTech
                })
                .FirstOrDefaultAsync();

            return result;
        }
    }





}



