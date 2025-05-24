using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TechWOController : Controller
    {
        private readonly ITechWOService _techWOService;
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;

        public TechWOController(ITechWOService techWOService, IQuickBooksConnectionService quickBooksConnectionService)
        {
            _techWOService = techWOService;
            _quickBooksConnectionService = quickBooksConnectionService; 
        }




        [HttpPost("InsertTechWO")]
        public async Task<IActionResult> InsertWorkOrderFee([FromBody] DTOs.TechnicianWorkOrder techWODTO)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _techWOService.InsertTechWOAsync(techWODTO);

                if (!result)
                {
                    return BadRequest("Unable to insert work order fee");
                }

                return Ok("Work Order Fee updated successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating technician work order {techWODTO.SheetID}: {ex.Message}");
                return StatusCode(500, "An error occurred while inserting technician work order.");
            }
        }

      




    }
}
