using Microsoft.AspNetCore.Identity.Data;
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

        [HttpPost("verify-login")]
        public async Task<IActionResult> VerifyLogin([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Email and password are required.");

            try
            {
                bool isValid = await _permissionsService.VerifyLoginAsync(request.Email, request.Password);

                if (!isValid)
                    return Unauthorized("Invalid email or password.");

                return Ok(new { success = true, message = "Login successful." });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error verifying login for {Email}", request.Email);
                return StatusCode(500, "Internal server error.");
            }
        }

    }
       
}
