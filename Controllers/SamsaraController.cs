using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SamsaraController : Controller
    {
        private readonly ISamsaraApiService _samsaraApiService;
        private readonly IVehicleService _vehicleService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;

        public SamsaraController(ISamsaraApiService samsaraAPIService, IQuickBooksConnectionService quickBooksConnectionService, IVehicleService vehicleService)
        {
            _samsaraApiService = samsaraAPIService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _vehicleService = vehicleService;
        }

        [HttpGet("GetVehicleInfo")]
        public async Task<IActionResult> GetVehicleInfoById(string vehicleID)
        {
            try
            {
                var vehicleData = await _samsaraApiService.GetVehicleByID(vehicleID);
 
                if (vehicleData == null)
                {
                    return NotFound("Vehicle not found."); // Returns 404 if no inventory parts are found
                }
     

                return Ok(vehicleData); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("GetAllVehicles")]
        public async Task<IActionResult> GetAllVehicles()
        {
            try
            {
                var vehicleData = await _samsaraApiService.GetAllVehicles();

                if (vehicleData == null)
                {
                    return NotFound("Vehicle not found."); // Returns 404 if no inventory parts are found
                }

                await _vehicleService.UpdateOrInsertVehiclesAsync(vehicleData); // Calls the service method to update or insert the vehicle data
                await _vehicleService.UpdateVehicleLog(1, 5); // Calls the service method to update the vehicle log
                return Ok(vehicleData); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


    }
}
