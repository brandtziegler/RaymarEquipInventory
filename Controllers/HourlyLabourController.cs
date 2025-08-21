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
        [HttpPost("SyncRegularLabourForSheet")]
        public IActionResult SyncRegularLabourForSheet(
            [FromBody] RegularLabourLineGroup labourGroup,
            [FromQuery] int sheetId,
            CancellationToken ct)
        {
            // STEP 0: sanity (don’t send me an empty cooler)
            if (sheetId <= 0) return BadRequest("sheetId is required.");

            var lines = labourGroup?.RegLabourLineList ?? new List<RegularLabourLine>();
            var intendedInsert = lines.Count;



            // STEP 2: kick the jobs — delete first, then insert (let Hangfire do the hauling)
            var deleteJobId = _jobs.Enqueue(() =>
                _hourlylabourService.DeleteRegularLabourAsync(sheetId, CancellationToken.None));

            string? insertJobId = null;
            if (intendedInsert > 0)
            {
                // use bulk wrapper that calls your existing InsertRegularLabourAsync per line
                var payload = lines; // Hangfire will serialize this DTO
                insertJobId = _jobs.ContinueJobWith(deleteJobId, () =>
                    _hourlylabourService.InsertRegularLabourBulkAsync(payload, CancellationToken.None));
            }

            // STEP 3: get out quick (202 Accepted)
            return Accepted(new
            {
                sheetId,
                intendedInsert,
                deleteJobId,
                insertJobId
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
