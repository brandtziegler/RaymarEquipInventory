﻿using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HourlyLabourController : Controller
    {
        private readonly IHourlyLabourService _hourlylabourService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;

        public HourlyLabourController(IHourlyLabourService hourlyLabourService, IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService)
        {
            _hourlylabourService = hourlyLabourService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
        }

        [HttpGet("GetHourlyLabourTypes")]
        public async Task<IActionResult> GetHourlyLabourTypes()
        {
            try
            {
                //var vehicleData = await _samsaraApiService.GetVehicleByID("281474986627612");
                List<HourlyLabourType> labourDetails = await _hourlylabourService.GetAllHourlyLabourTypes();

                if (labourDetails == null || labourDetails.Count == 0)
                {
                    return NotFound("No hourly labour types found."); // Returns 404 if no inventory parts are found
                }


                return Ok(labourDetails); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


        [HttpPost("ClearRegularLabour")]
        public async Task<IActionResult> ClearRegularLabour([FromQuery] int technicianWorkOrderId)
        {
            try
            {
                var result = await _hourlylabourService.DeleteRegularLabourAsync(technicianWorkOrderId);
                if (!result)
                    return BadRequest("Failed to clear regular labour entries.");

                return Ok("Regular labour entries cleared successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"🔥 Error clearing regular labour for TechWO ID {technicianWorkOrderId}: {ex.Message}");
                return StatusCode(500, "An error occurred during regular labour clearing.");
            }
        }

        [HttpPost("AddRegularLabourBatch")]
        public async Task<IActionResult> AddRegularLabourBatch([FromBody] RegularLabourLineGroup labourGroup)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return BadRequest(new { errors });
            }

            bool allSucceeded = true;

            foreach (var labour in labourGroup.RegLabourLineList)
            {
                var result = await _hourlylabourService.InsertRegularLabourAsync(labour);
                if (!result)
                {
                    allSucceeded = false;
                }
            }

            return allSucceeded
                ? Ok("✅ All regular labour lines inserted successfully.")
                : StatusCode(207, "⚠️ Some labour lines failed to insert.");
        }

        [HttpGet("GetHourlyLabourById")]
        public async Task<IActionResult> GetHourlyLabourById(int hourlyLabourID)
        {
            try
            {
                HourlyLabourType labourData = await _hourlylabourService.GetHourlyLabourById(hourlyLabourID);
                if (labourData == null)
                {
                    return NotFound("No labour with this ID found."); // Returns 404 if no customer is found
                }
                return Ok(labourData);
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");

            }
        }
    }
       
}
