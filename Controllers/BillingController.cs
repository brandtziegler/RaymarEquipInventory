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




        [HttpPost("UpsertBilling")]
        public async Task<IActionResult> UpsertBilling([FromBody] Billing billingDTO)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                bool result;

                if (billingDTO.SheetId == 0)
                {
                    result = await _billingService.InsertBillingInformationAsync(billingDTO);
                    if (!result)
                        return BadRequest("Unable to insert billing.");
                }
                else
                {
                    result = await _billingService.UpdateBillingInformationAsync(billingDTO);
                    if (!result)
                        return BadRequest("Unable to update billing.");
                }

                return Ok("Work Order Fee processed successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"🔥 Billing operation failed for CustPath {billingDTO.CustPath}: {ex.Message}");
                return StatusCode(500, "An error occurred while processing billing information.");
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
