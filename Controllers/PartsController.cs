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


        [HttpGet("GetPartsByWorkOrder")]
        public async Task<IActionResult> GetPartsByWorkOrder(int sheetID)
        {
            try
            {
                List<PartsUsed> partsUsedData = await _partService.GetPartsByWorkOrder(sheetID);
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


        [HttpGet("GetPartsByWorkOrderTwo")]
        public async Task<IActionResult> GetPartsByWorkOrderTwo(int sheetID)
        {
            try
            {
                List<PartsUsed> partsUsedData = await _partService.GetPartsByWorkOrder(sheetID);
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

    }
}
