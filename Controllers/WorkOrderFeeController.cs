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




        [HttpPost("InsertWorkOrderFee")]
        public async Task<IActionResult> InsertWorkOrderFee([FromBody] WorkOrderFee workOrderFeeDTO)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderFeeService.InsertWorkOrderFee(workOrderFeeDTO);

                if (!result)
                {
                    return BadRequest("Unable to insert work order fee");
                }

                return Ok("Work Order Fee updated successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating work order fee {workOrderFeeDTO.FlatLabourID}: {ex.Message}");
                return StatusCode(500, "An error occurred while inserting into work order fee.");
            }
        }

      




    }
}
