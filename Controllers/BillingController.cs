using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BillingController : Controller
    {
        private readonly IBillingService _billingService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;

        public BillingController(IBillingService billingService, IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService)
        {
            _billingService = billingService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
        }


        [HttpPost("UpdateBill")]
        public async Task<IActionResult> UpdateBill([FromBody] Billing billingDto)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _billingService.UpdateBillingInfo(billingDto);

                if (!result)
                {
                    return BadRequest("Unable to update bill with the provided billing information.");
                }

                return Ok("Bill was updated successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating Bill: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

        [HttpGet("GetBillingForWorkOrder")]
        public async Task<IActionResult> GetBillingForWorkOrder(int sheetID)
        {
            try
            {
                //var vehicleData = await _samsaraApiService.GetVehicleByID("281474986627612");
                Billing billingInfo = await _billingService.GetLabourForWorkorder(sheetID);

                if (billingInfo == null )
                {
                    return NotFound("No billing info found."); // Returns 404 if no inventory parts are found
                }


                return Ok(billingInfo); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

    }
       
}
