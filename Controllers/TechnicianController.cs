using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TechnicianController : Controller
    {
        private readonly ITechnicianService _technicianService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;

        public TechnicianController(ITechnicianService technicianService, IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService)
        {
            _technicianService = technicianService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
        }



        [HttpGet("GetTechniciansByID")]
        public async Task<IActionResult> GetTechniciansByID(Int32 techID)
        {
            try
            {
                Tech techData = await _technicianService.GetTechByID(techID);
                if (techData == null)
                {
                    return NotFound("No tech with this ID found."); // Returns 404 if no customer is found
                }
                return Ok(techData);
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");

            }
        }
    }
       
}
