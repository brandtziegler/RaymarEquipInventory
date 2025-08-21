using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;
using Hangfire;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MileageAndTravelController : Controller
    {
        private readonly IMileageAndTravelService _mileageTravelService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;
        private readonly IBackgroundJobClient _jobs;

        public MileageAndTravelController(IMileageAndTravelService mileageTravelService, IQuickBooksConnectionService quickBooksConnectionService,
            IBackgroundJobClient jobs, ISamsaraApiService samsaraApiService)
        {
            _mileageTravelService = mileageTravelService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
            _jobs = jobs;
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


        // POST /api/MileageAndTravel/InsertTravelLogBatchForSheet?sheetId=123
        // Always enqueue: clear -> bulk insert -> ensure segments
        // POST /api/MileageAndTravel/InsertTravelLogBatchForSheet?sheetId=123
        // Always enqueue: clear -> (insert if any) -> ensure segments
        [HttpPost("InsertTravelLogBatchForSheet")]
        public IActionResult InsertTravelLogBatchForSheet(
            [FromQuery] int sheetId,
            [FromBody] List<TravelLog> entries)
        {
            if (sheetId <= 0) return BadRequest("sheetId is required.");
            entries ??= new List<TravelLog>();
            var intendedInsert = entries.Count;

            // Normalize: server is source of truth for SheetId
            if (intendedInsert > 0)
            {
                foreach (var e in entries)
                    if (e.SheetId <= 0 || e.SheetId != sheetId) e.SheetId = sheetId;
            }

            // 1) Clear
            var clearJobId = _jobs.Enqueue(() =>
                _mileageTravelService.DeleteTravelLogAsync(sheetId, CancellationToken.None));

            // 2) Insert (only if any), continue only if clear succeeded
            string? insertJobId = null;
            if (intendedInsert > 0)
            {
                var payload = entries; // serialized by Hangfire
                insertJobId = _jobs.ContinueJobWith(
                    clearJobId,
                    () => _mileageTravelService.InsertTravelLogBulkAsync(payload, CancellationToken.None),
                    JobContinuationOptions.OnlyOnSucceededState);
            }

            // 3) Ensure segments, after whichever job ran last — only if prior succeeded
            var tailJob = insertJobId ?? clearJobId;
            var ensureJobId = _jobs.ContinueJobWith(
                tailJob,
                () => _mileageTravelService.EnsureThreeSegmentsAsync(sheetId, CancellationToken.None),
                JobContinuationOptions.OnlyOnSucceededState);

            // 4) Return fast
            return Accepted(new
            {
                sheetId,
                intendedInsert,
                clearJobId,
                insertJobId,
                ensureJobId,
                message = intendedInsert == 0
                    ? "Queued clear + ensure (no entries)."
                    : $"Queued clear + insert ({intendedInsert}) + ensure."
            });
        }


    }

}
