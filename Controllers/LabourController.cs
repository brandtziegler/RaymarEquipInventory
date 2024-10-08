using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LabourController : Controller
    {
        private readonly ILabourService _labourService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;

        public LabourController(ILabourService labourService, IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService)
        {
            _labourService = labourService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
        }

        [HttpGet("GetLabourForWorkOrder")]
        public async Task<IActionResult> GetLabourForWorkorder(int sheetID)
        {
            try
            {
                //var vehicleData = await _samsaraApiService.GetVehicleByID("281474986627612");
                List<LabourLine> labourDetails = await _labourService.GetLabourByWorkOrder(sheetID);

                if (labourDetails == null || labourDetails.Count == 0)
                {
                    return NotFound("No labour found."); // Returns 404 if no inventory parts are found
                }


                return Ok(labourDetails); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }



        [HttpPost("UpdateLabour")]
        public async Task<IActionResult> UpdateLabour([FromBody] LabourLine labourDTO)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _labourService.UpdateLabour(labourDTO);

                if (!result)
                {
                    return BadRequest("Unable to update labour with the provided information.");
                }

                return Ok("Labour updated successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating labour: {ex.Message}");
                return StatusCode(500, "An error occurred while updating labour.");
            }
        }

        [HttpGet("GetLabourByID")]
        public async Task<IActionResult> GetLabourByID(int labourID)
        {
            try
            {
                LabourLine labourData = await _labourService.GetLabourById(labourID);
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
