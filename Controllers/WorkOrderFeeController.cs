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
            if (technicianWorkOrderId <= 0)
                return BadRequest("technicianWorkOrderId is required.");
            if (feeGroup is null)
                return BadRequest("Payload is required.");

            var items = feeGroup.WorkOrderFeesList ?? new List<WorkOrderFee>();
            var intendedInsert = items.Count;

            // Normalize: server is source of truth for TechWO id
            if (intendedInsert > 0)
            {
                foreach (var f in items)
                {
                    if (f.TechnicianWorkOrderID <= 0)
                        f.TechnicianWorkOrderID = technicianWorkOrderId;
                }
            }

            // 1) Enqueue clear
            var clearJobId = _jobs.Enqueue(() =>
                _workOrderFeeService.DeleteWorkOrderFees(technicianWorkOrderId, CancellationToken.None));

            // 2) Chain insert (only if we have items)
            string? insertJobId = null;
            if (intendedInsert > 0)
            {
                var payload = items; // Hangfire will serialize DTOs
                insertJobId = _jobs.ContinueJobWith(clearJobId, () =>
                    _workOrderFeeService.InsertWorkOrderFeeBulkAsync(payload, CancellationToken.None));
            }

            // 3) Return fast
            return Accepted(new
            {
                technicianWorkOrderId,
                intendedInsert,
                clearJobId,
                insertJobId,
                message = intendedInsert == 0
                    ? "Queued clear only (no fees in payload)."
                    : $"Queued clear + insert of {intendedInsert} WorkOrderFee row(s)."
            });
        }



    }
}
