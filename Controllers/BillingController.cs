using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;
using Hangfire;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BillingController : Controller
    {
        private readonly IBillingService _billingService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;
        private readonly IBackgroundJobClient _jobs;
        public BillingController(IBillingService billingService, IQuickBooksConnectionService quickBooksConnectionService, IBackgroundJobClient jobs,
            ISamsaraApiService samsaraApiService)
        {
            _billingService = billingService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
            _jobs = jobs;
        }




        [HttpPost("UpsertBilling")]
        public IActionResult UpsertBilling([FromBody] Billing billingDTO)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                // 1) Enqueue the insert-if-needed
                var insertJobId = _jobs.Enqueue(() =>
                    _billingService.TryInsertBillingInformationAsync(billingDTO, CancellationToken.None));

                // 2) Chain the update after insert completes
                var updateJobId = _jobs.ContinueJobWith(insertJobId, () =>
                    _billingService.UpdateBillingInformationAsync(billingDTO, CancellationToken.None));

                // 3) Return immediately with the job IDs
                return Accepted(new
                {
                    insertJobId,
                    updateJobId,
                    message = "Queued billing upsert (insert then update)."
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 Billing enqueue failed for CustPath {CustPath}", billingDTO.CustPath);
                return StatusCode(500, "An error occurred while enqueuing billing.");
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
