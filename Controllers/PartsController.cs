using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PartsController : Controller
    {
        private readonly IPartService _partService;
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;

        public PartsController(IPartService partService, IQuickBooksConnectionService quickBooksConnectionService)
        {
            _partService = partService;
            _quickBooksConnectionService = quickBooksConnectionService; 
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


        [HttpPost("AddPartsUsed")]
        public async Task<IActionResult> AddPartsUsed([FromBody] PartsUsed entry)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(new { errors });
            }

            var success = await _partService.InsertPartsUsedAsync(entry);
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
