using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkOrdController : Controller
    {
        private readonly IWorkOrderService _workOrderService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;

        public WorkOrdController(IWorkOrderService workOrderService, IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService)
        {
            _workOrderService = workOrderService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
        }


        [HttpPost("LaunchWorkOrder")]
        public async Task<IActionResult> LaunchWorkOrder([FromBody] Billing billingDto)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.LaunchWorkOrder(billingDto);

                if (!result)
                {
                    return BadRequest("Unable to launch work order with the provided billing information.");
                }

                return Ok("Work order launched successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error launching work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

        //[HttpGet("GetLabourForWorkOrder")]
        //public async Task<IActionResult> GetLabourForWorkorder(int sheetID)
        //{
        //    try
        //    {
        //        //var vehicleData = await _samsaraApiService.GetVehicleByID("281474986627612");
        //        List<LabourLine> labourDetails = await _labourService.GetLabourByWorkOrder(sheetID);

        //        if (labourDetails == null || labourDetails.Count == 0)
        //        {
        //            return NotFound("No labour found."); // Returns 404 if no inventory parts are found
        //        }


        //        return Ok(labourDetails); // Returns a 200 status code with the inventory data
        //    }
        //    catch (Exception ex)
        //    {
        //        // Catching the exception and returning a 500 error with the message
        //        return StatusCode(500, $"Internal server error: {ex.Message}");
        //    }
        //}

    }
       
}
