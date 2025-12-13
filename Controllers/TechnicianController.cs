using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TechnicianController : Controller
    {
        private readonly ITechnicianService _technicianService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;

        public TechnicianController(ITechnicianService technicianService, IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService)
        {
            _technicianService = technicianService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
        }



        [HttpGet("GetTechniciansByID")]
        public async Task<IActionResult> GetTechniciansByID(int techID)
        {
            ///just one more change to test. now test this.----
            try
            {
                Tech techData = await _technicianService.GetTechByID(techID);
                if (techData == null)
                {
                    return NotFound("No tech with this ID found."); // Returns 404 if no customer is found
                }
                return Ok(techData);
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");

            }
        }

        [HttpGet("GetAllTechnicians")]  
        public async Task<IActionResult> GetAllTechnicians()
        {
            try
            {
                //var vehicleData = await _samsaraApiService.GetVehicleByID("281474986627612");
                List<Tech> techs = await _technicianService.GetAllTechs();

                if (techs == null || techs.Count == 0)
                {
                    return NotFound("No techs found."); // Returns 404 if no inventory parts are found
                }


                return Ok(techs); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }



        [HttpGet("GetSettingsPeople")]
        public async Task<IActionResult> GetSettingsPeople()
        {
            try
            {
                var people = await _technicianService.GetSettingsPeople();

                if (people == null || people.Count == 0)
                    return NotFound("No people found.");

                return Ok(people);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("UpsertSettingsPerson")]
        public async Task<IActionResult> UpsertSettingsPerson([FromBody] SettingsPersonDto dto)
        {
            if (dto == null) return BadRequest("Body required.");

            try
            {
                var saved = await _technicianService.UpsertSettingsPerson(dto);
                return Ok(saved); // return refreshed row from vw_PersonWithTechProfiles
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                // e.g. duplicate email, missing FlatLabour mapping, person not found, etc.
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


        [HttpGet("GetTechsByWorkOrder")]
        public async Task<IActionResult> GetTechsByWorkOrder(int sheetID)
        {
            try
            {
                //var vehicleData = await _samsaraApiService.GetVehicleByID("281474986627612");
                List<Tech> techs = await _technicianService.GetTechsByWorkOrder(sheetID);

                if (techs == null || techs.Count == 0)
                {
                    return NotFound("No techs found."); // Returns 404 if no inventory parts are found
                }


                return Ok(techs); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

    }
       
}
