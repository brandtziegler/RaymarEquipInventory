using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PermissionsController : Controller
    {
        private readonly IPermissionsService _permissionsService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;

        public PermissionsController(IPermissionsService hourlyLabourService, IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService)
        {
            _permissionsService = hourlyLabourService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
        }

        [HttpGet("{technicianId}")]
        public async Task<ActionResult<DTOs.RolesAndPermissions>> GetPermissions(int technicianId)
        {
            var permissions = await _permissionsService.GetPermissionsByTechnicianIdAsync(technicianId);

            if (permissions == null)
                return NotFound($"Technician {technicianId} not found or not permissioned.");

            return Ok(permissions);
        }

    }
       
}
