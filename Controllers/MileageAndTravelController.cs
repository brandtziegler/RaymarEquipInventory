using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MileageAndTravelController : Controller
    {
        private readonly IMileageAndTravelService _mileageTravelService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;

        public MileageAndTravelController(IMileageAndTravelService mileageTravelService, IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService)
        {
            _mileageTravelService = mileageTravelService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
        }



        [HttpPost("AddTravelLog")]
        public async Task<IActionResult> AddTravelLog([FromBody] TravelLog travelLog)
        {
            try
            {
                var result = await _mileageTravelService.InsertTravelLogAsync(travelLog);

                if (!result)
                {
                    return BadRequest("Unable to insert travel log.");
                }

                return Ok("Travel Log inserted successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error inserting travel log: {ex.Message}");
                return StatusCode(500, "An error occurred while inserting travel log.");
            }
        }


        [HttpGet("GetTravelLogByID")]
        public async Task<IActionResult> GetTravelLogByID(int travelLogID)
        {
            try
            {
                TravelLog travelLog = await _mileageTravelService.GetTravelByID(travelLogID);
                if (travelLog == null)
                {
                    return NotFound("No labour with this ID found."); // Returns 404 if no customer is found
                }
                return Ok(travelLog);
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");

            }
        }
    }
       
}
