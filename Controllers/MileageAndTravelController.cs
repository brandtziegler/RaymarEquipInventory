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

        [HttpPost("ClearMileageEntries")]
        public async Task<IActionResult> ClearMileageEntries([FromQuery] int sheetId)
        {
            try
            {
                var result = await _mileageTravelService.DeleteTravelLogAsync(sheetId);
                if (!result)
                    return BadRequest("Failed to clear mileage entries.");

                return Ok("Mileage entries cleared successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"🔥 Error clearing mileage entries for SheetID {sheetId}: {ex.Message}");
                return StatusCode(500, "An error occurred during mileage clearing.");
            }
        }

        [HttpPost("AddTravelLog")]
        public async Task<IActionResult> AddTravelLogEntry([FromBody] TravelLog entry)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(new { errors });
            }

            var success = await _mileageTravelService.InsertTravelLogAsync(entry);
            return success ? Ok() : BadRequest("Insert failed.");
        }

        [HttpPost("EnsureMileageSegments")]
        public async Task<IActionResult> EnsureMileageSegments([FromQuery] int sheetId)
        {
            await _mileageTravelService.EnsureThreeSegmentsAsync(sheetId);
            return Ok("✅ Segments patched if missing.");
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
