using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;
using Hangfire;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PartsController : Controller
    {
        private readonly IPartService _partService;
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly IBackgroundJobClient _jobs;

        public PartsController(IPartService partService, IQuickBooksConnectionService quickBooksConnectionService, IBackgroundJobClient jobs)
        {
            _partService = partService;
            _quickBooksConnectionService = quickBooksConnectionService; 
            _jobs = jobs;
        }



        [HttpGet("GetPartById")]
        public async Task<IActionResult> GetPartById(int partID)
        {
            try
            {
                PartsUsed partsUsedData = await _partService.GetPartByID(partID);
                if (partsUsedData == null)
                {
                    return NotFound("No customer with this ID found."); // Returns 404 if no customer is found
                }
                return Ok(partsUsedData);
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");

            }
        }

        [HttpPost("UpdatePart")]
        public async Task<IActionResult> UpdatePart([FromBody] PartsUsed partDTO)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _partService.UpdatePart(partDTO);

                if (!result)
                {
                    return BadRequest("Unable to update part");
                }

                return Ok("Part updated successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating part {partDTO.InventoryData.InventoryId}: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }


        // POST /api/Parts/InsertPartsUsedBatchForSheet?sheetId=123
        // Always enqueue: clear existing PartsUsed for the sheet, then bulk-insert the provided list.
        [HttpPost("InsertPartsUsedBatchForSheet")]
        public IActionResult InsertPartsUsedBatchForSheet(
            [FromQuery] int sheetId,
            [FromBody] PartsUsedGroup groupEntry)
        {
            if (sheetId <= 0)
                return BadRequest("sheetId is required.");
            if (groupEntry is null)
                return BadRequest("Payload is required.");

            var items = groupEntry.PartsUsedList ?? new List<PartsUsed>();
            var intendedInsert = items.Count;

            // Normalize: server is source of truth for SheetId
            if (intendedInsert > 0)
            {
                foreach (var p in items)
                {
                    if (p.SheetId is null || p.SheetId == 0 || p.SheetId != sheetId)
                        p.SheetId = sheetId;
                }
            }

            // 1) Enqueue clear
            var clearJobId = _jobs.Enqueue(() =>
                _partService.ClearPartsUsedAsync(sheetId, CancellationToken.None));

            // 2) Chain bulk insert (if any)
            string? insertJobId = null;
            if (intendedInsert > 0)
            {
                var payload = items; // Hangfire will serialize the DTO list
                insertJobId = _jobs.ContinueJobWith(clearJobId, () =>
                    _partService.InsertPartsUsedBulkAsync(payload, CancellationToken.None));
            }

            // 3) Return fast
            return Accepted(new
            {
                sheetId,
                intendedInsert,
                clearJobId,
                insertJobId,
                message = intendedInsert == 0
                    ? "Queued clear only (no parts in payload)."
                    : $"Queued clear + insert of {intendedInsert} PartsUsed row(s)."
            });
        }


        [HttpPost("ClearPartsUsedAsync")]
        public async Task<IActionResult> ClearPartsUsedAsync([FromQuery] int sheetId)
        {

            // 🧹 Step 1: Clear old parts for this SheetID
            var success = await _partService.ClearPartsUsedAsync(sheetId); // <-- implement this in your service/repo layer

            // 🧱 Step 2: Insert new parts

            return success ? Ok() : BadRequest("Insert failed.");
        }

        [HttpPost("AddPartsUsed")]
        public async Task<IActionResult> AddPartsUsed([FromQuery] int sheetId, [FromBody] PartsUsedGroup groupEntry)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(new { errors });
            }

            var success = true;

            foreach (PartsUsed pu in groupEntry.PartsUsedList)
            {
                if (!await _partService.InsertPartsUsedAsync(pu))
                {
                    success = false;
                }
            }

            return success ? Ok() : BadRequest("Insert failed.");
        }

        [HttpGet("GetPartsByWorkOrder")]
        public async Task<IActionResult> GetPartsByWorkOrder(
            int sheetID,
            int pageNumber = 1,
            int pageSize = 20,
            string itemName = null,
            int? qtyUsedMin = null,
            int? qtyUsedMax = null,
            string manufacturerPartNumber = null,
            string sortBy = "itemName", // Default sort field
            string sortDirection = "asc" // Default sort direction
        )
        {
            try
            {
                //small change, partscontroller.
                var partsUsed = await _partService.GetPartsByWorkOrder(
                    sheetID, pageNumber, pageSize, itemName, qtyUsedMin, qtyUsedMax, manufacturerPartNumber, sortBy, sortDirection);

                return Ok(partsUsed);
            }
            catch (Exception ex)
            {
                // Log the exception as necessary
                return StatusCode(500, "An error occurred while retrieving parts.");
            }
        }

        [HttpGet("GetPartsCountByWorkOrder")]
        public async Task<IActionResult> GetPartsCountByWorkOrder(
    int sheetID,
    string itemName = null,
    int? qtyUsedMin = null,
    int? qtyUsedMax = null,
    string manufacturerPartNumber = null)
        {
            try
            {
                //one small comment change.
                int count = await _partService.GetPartsCountByWorkOrder(sheetID, itemName, qtyUsedMin, qtyUsedMax, manufacturerPartNumber);
                return Ok(count);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }





    }
}
