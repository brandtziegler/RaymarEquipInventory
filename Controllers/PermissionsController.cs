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

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto dto)
        {
            var result = await _permissionsService.ResetPasswordAsync(dto.Email, dto.CurrentPassword, dto.NewPassword);
            return result.Success ? Ok(result) : Unauthorized(result);
        }

        [HttpPost("verify-login")]
        public async Task<IActionResult> VerifyLogin([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Email and password are required.");

            try
            {
                // ✅ The service now returns a DTO, not a bool-comment
                var result = await _permissionsService.VerifyLoginAsync(request.Email, request.Password);

                if (result == null || !result.Success)
                    return Unauthorized(new { success = false, message = "Invalid email or password." });

                // ✅ Return the DTO directly (serialized automatically)
                return Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error verifying login for {Email}", request.Email);
                return StatusCode(500, new { success = false, message = "Internal server error." });
            }
        }


    }

}
