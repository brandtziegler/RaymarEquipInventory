using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;
using Hangfire;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HourlyLabourController : Controller
    {
        private readonly IHourlyLabourService _hourlylabourService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;
        private readonly IBackgroundJobClient _jobs;

        public HourlyLabourController(IHourlyLabourService hourlyLabourService, 
            IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService, IBackgroundJobClient jobs)
        {
            _hourlylabourService = hourlyLabourService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
            _jobs = jobs;
        }

        [HttpGet("GetHourlyLabourTypes")]
        public async Task<IActionResult> GetHourlyLabourTypes()
        {
            try
            {
                //var vehicleData = await _samsaraApiService.GetVehicleByID("281474986627612");
                List<HourlyLabourType> labourDetails = await _hourlylabourService.GetAllHourlyLabourTypes();

                if (labourDetails == null || labourDetails.Count == 0)
                {
                    return NotFound("No hourly labour types found."); // Returns 404 if no inventory parts are found
                }


                return Ok(labourDetails); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


        [HttpPost("ClearAndUpsertRegularLabour")]
        public async Task<IActionResult> ClearAndUpsertRegularLabour([FromBody] RegularLabourLineGroup labourGroup, [FromQuery] int sheetId, CancellationToken ct)
        {
            try
            {
                var deleted = await _hourlylabourService.DeleteRegularLabourAsync(sheetId, ct);

                return Ok(new
                {
                    sheetId,
                    deleted,
                    message = deleted == 0
                        ? "No regular labour entries found for this sheet."
                        : $"Deleted {deleted} RegularLabour entries."
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 Error clearing regular labour for SheetID {SheetId}", sheetId);
                return StatusCode(500, "An error occurred during regular labour clearing.");
            }
        }



        // POST /api/WorkOrd/SyncRegularLabourForSheet?sheetId=123//mewcomment
        // POST /api/HourlyLabour/ClearRegularLabourForSheet?sheetId=123
        [HttpPost("ClearRegularLabourForSheet")]
        public async Task<IActionResult> ClearRegularLabourForSheet([FromQuery] int sheetId, CancellationToken ct)
        {
            if (sheetId <= 0) return BadRequest("sheetId is required.");

            var deleted = await _hourlylabourService.DeleteRegularLabourAsync(sheetId, ct);

            return Ok(new
            {
                sheetId,
                deleted,
                message = deleted == 0
                    ? "No regular labour entries found for this sheet."
                    : $"Deleted {deleted} RegularLabour entries."
            });
        }

        // POST /api/HourlyLabour/InsertRegularLabourBatchForSheet?sheetId=123
        // Sends the lines you want inserted AFTER you've cleared. Default: synchronous insert.
        // If you still want background mode sometimes, set enqueue=true.
        [HttpPost("InsertRegularLabourBatchForSheet")]
        public async Task<IActionResult> InsertRegularLabourBatchForSheet(
            [FromBody] RegularLabourLineGroup labourGroup,
            [FromQuery] int sheetId,
            [FromQuery] bool enqueue = false,                 // default sync for atomic testing
            CancellationToken ct = default)
        {
            if (sheetId <= 0) return BadRequest("sheetId is required.");
            var lines = labourGroup?.RegLabourLineList ?? new List<RegularLabourLine>();
            var intendedInsert = lines.Count;
            var deleted = await _hourlylabourService.DeleteRegularLabourAsync(sheetId, ct);
            // (Optional) If you want to hard-enforce sheet ownership, validate here.
            // You said these are pre-verified, so we skip extra checks.

            if (!enqueue)
            {
                var inserted = await _hourlylabourService.InsertRegularLabourBulkAsync(lines, ct);
                return Ok(new
                {
                    sheetId,
                    intendedInsert,
                    inserted,
                    message = $"Inserted {inserted} RegularLabour entries."
                });
            }

            // Background path (if you flip enqueue=true)
            var jobId = _jobs.Enqueue(() =>
                _hourlylabourService.InsertRegularLabourBulkAsync(lines, CancellationToken.None));

            return Accepted(new
            {
                sheetId,
                intendedInsert,
                jobId,
                message = $"Queued insert of {intendedInsert} RegularLabour entries."
            });
        }


        [HttpPost("AddRegularLabourBatch")]
        public async Task<IActionResult> AddRegularLabourBatch([FromBody] RegularLabourLineGroup labourGroup)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(new { errors });
            }

            bool allSucceeded = true;

            foreach (var labour in labourGroup.RegLabourLineList)
            {
                var result = await _hourlylabourService.InsertRegularLabourAsync(labour);
                if (!result)
                {
                    allSucceeded = false;
                }
            }

            return allSucceeded
                ? Ok("✅ All regular labour lines inserted successfully.")
                : StatusCode(207, "⚠️ Some labour lines failed to insert.");
        }

        [HttpGet("GetHourlyLabourById")]
        public async Task<IActionResult> GetHourlyLabourById(int hourlyLabourID)
        {
            try
            {
                HourlyLabourType labourData = await _hourlylabourService.GetHourlyLabourById(hourlyLabourID);
                if (labourData == null)
                {
                    return NotFound("No labour with this ID found."); // Returns 404 if no customer is found
                }
                return Ok(labourData);
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");

            }
        }
    }
       
}
