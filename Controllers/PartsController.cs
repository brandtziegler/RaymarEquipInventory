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
        public async Task<IActionResult> GetPartsByWorkOrder(
            int sheetID,
            int pageNumber = 1,
            int pageSize = 5,
            string itemName = null,
            int? qtyUsedMin = null,
            int? qtyUsedMax = null,
            string manufacturerPartNumber = null)
        {
            try
            {
                // Call the service method with pagination and filtering
                List<PartsUsed> partsUsedData = await _partService.GetPartsByWorkOrder(
                    sheetID,
                    pageNumber,
                    pageSize,
                    itemName,
                    qtyUsedMin,
                    qtyUsedMax,
                    manufacturerPartNumber);

                if (partsUsedData == null || !partsUsedData.Any())
                {
                    return NotFound("No parts found for this work order."); // Returns 404 if no parts are found
                }

                return Ok(partsUsedData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
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
