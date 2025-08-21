using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkOrderFeeController : Controller
    {
        private readonly IWorkOrderFeeService _workOrderFeeService;
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;

        public WorkOrderFeeController(IWorkOrderFeeService workOrderFeeService, IQuickBooksConnectionService quickBooksConnectionService)
        {
            _workOrderFeeService = workOrderFeeService;
            _quickBooksConnectionService = quickBooksConnectionService; 
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

        [HttpPost("InsertWorkOrderFeeBatchForTechWO")]
        public async Task<IActionResult> InsertWorkOrderFeeBatchForTechWO(
            [FromQuery] int technicianWorkOrderId,
            [FromBody] WorkOrderFeesGroup feeGroup,
            CancellationToken ct = default)
        {
            if (technicianWorkOrderId <= 0) return BadRequest("technicianWorkOrderId is required.");
            if (feeGroup is null) return BadRequest("Payload is required.");

            var items = feeGroup.WorkOrderFeesList ?? new List<WorkOrderFee>();
            // Normalize TechWO on each inbound row
            foreach (var f in items)
                if (f.TechnicianWorkOrderID <= 0) f.TechnicianWorkOrderID = technicianWorkOrderId;

            // Clear first (sync)
            var cleared = await _workOrderFeeService.DeleteWorkOrderFees(technicianWorkOrderId, ct);
            if (!cleared) return StatusCode(500, new { technicianWorkOrderId, message = "Failed to clear existing fees." });

            // One-shot bulk insert (sync)
            var inserted = await _workOrderFeeService.InsertWorkOrderFeeBulkAsync(items, ct);

            return Ok(new
            {
                technicianWorkOrderId,
                cleared,
                intendedInsert = items.Count,
                inserted,
                message = items.Count == 0
                    ? "Cleared fees; no new rows provided."
                    : $"Cleared fees; inserted {inserted} WorkOrderFee row(s)."
            });
        }


    }
}
