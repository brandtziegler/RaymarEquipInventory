using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;
using Hangfire;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkOrderFeeController : Controller
    {
        private readonly IWorkOrderFeeService _workOrderFeeService;
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly IBackgroundJobClient _jobs;
        public WorkOrderFeeController(IWorkOrderFeeService workOrderFeeService, IQuickBooksConnectionService quickBooksConnectionService, IBackgroundJobClient jobs)
        {
            _workOrderFeeService = workOrderFeeService;
            _quickBooksConnectionService = quickBooksConnectionService; 
            _jobs = jobs;
        }


        [HttpPost("ClearWorkOrderFees")]
        public async Task<IActionResult> ClearWorkOrderFees([FromQuery] int technicianWorkOrderId)
        {
            try
            {
                var result = await _workOrderFeeService.DeleteWorkOrderFees(technicianWorkOrderId);
                if (!result)
                    return BadRequest("Failed to clear work order fees.");

                return Ok("Work order fees cleared successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"🔥 Error clearing fees for TechWO ID {technicianWorkOrderId}: {ex.Message}");
                return StatusCode(500, "An error occurred during fee clearing.");
            }
        }

        [HttpPost("InsertWorkOrderFeeBatch")]
        public async Task<IActionResult> InsertWorkOrderFeeBatch([FromBody] WorkOrderFeesGroup feeGroup)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(new { errors });
            }

            var success = true;

            foreach (var fee in feeGroup.WorkOrderFeesList)
            {
                if (!await _workOrderFeeService.InsertWorkOrderFee(fee))
                {
                    success = false;
                }
            }

            return success ? Ok("Batch insert successful.") : BadRequest("One or more inserts failed.");
        }

        // POST /api/WorkOrd/InsertWorkOrderFeeBatchForTechWO?technicianWorkOrderId=123
        // Always enqueue: clear fees for the TechWO, then bulk-insert the provided batch.
        [HttpPost("InsertWorkOrderFeeBatchForTechWO")]
        public IActionResult InsertWorkOrderFeeBatchForTechWO(
          [FromQuery] int technicianWorkOrderId,
          [FromBody] WorkOrderFeesGroup feeGroup)
        {
            if (technicianWorkOrderId <= 0) return BadRequest("technicianWorkOrderId is required.");
            if (feeGroup is null) return BadRequest("Payload is required.");

            var items = feeGroup.WorkOrderFeesList ?? new List<WorkOrderFee>();
            var intendedInsert = items.Count;

            // normalize fees TechWO
            if (intendedInsert > 0)
                foreach (var f in items)
                    if (f.TechnicianWorkOrderID <= 0) f.TechnicianWorkOrderID = technicianWorkOrderId;

            // normalize visibility TechWO (optional in payload)
            var visItems = feeGroup.FeeVisibilityList ?? new List<FeeVisibilityDto>();
            foreach (var v in visItems)
                if (v.TechnicianWorkOrderID <= 0) v.TechnicianWorkOrderID = technicianWorkOrderId;
            var visCount = visItems.Count;

            // 1) Clear WOF
            var clearJobId = _jobs.Enqueue(() =>
                _workOrderFeeService.DeleteWorkOrderFees(technicianWorkOrderId, CancellationToken.None));

            // 2) Re-insert WOF
            string? insertJobId = null;
            if (intendedInsert > 0)
            {
                var payload = items;
                insertJobId = _jobs.ContinueJobWith(clearJobId, () =>
                    _workOrderFeeService.InsertWorkOrderFeeBulkAsync(payload, CancellationToken.None));
            }

            // 3) Replace FeeVisibility for this TechWO (delete then insert distinct)
            string? visJobId = null;
            if (visCount >= 0) // allow empty list to mean "reset to defaults (no rows = visible)"
            {
                var vp = visItems;
                visJobId = _jobs.Enqueue(() =>
                    _workOrderFeeService.ReplaceFeeVisibilityAsync(technicianWorkOrderId, vp, CancellationToken.None));
            }

            return Accepted(new
            {
                technicianWorkOrderId,
                intendedInsert,
                clearJobId,
                insertJobId,
                visCount,
                visJobId,
                message = $"{(intendedInsert == 0 ? "Queued clear only for fees" : $"Queued clear + insert of {intendedInsert} fees")}; " +
                          $"{(visCount == 0 ? "reset visibility to defaults" : $"replace {visCount} visibility rows")}."
            });
        }




    }
}
