using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;

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

    }
}
