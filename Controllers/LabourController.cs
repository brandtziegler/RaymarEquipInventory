using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LabourController : Controller
    {
        private readonly ILabourService _labourService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;

        public LabourController(ILabourService labourService, IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService)
        {
            _labourService = labourService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
        }

        //// Dummy endpoint for getting a product by ID
        //[HttpGet("{id}")]
        //public IActionResult GetProductById(int id)
        //{
        //    var product = _inventoryService.GetProductById(id);
        //    return Ok(product); // Returns a 200 status code with product details
        //}

        //// Dummy endpoint for getting all products
        //[HttpGet]
        //public IActionResult GetAllProducts()
        //{
        //    var products = _inventoryService.GetAllProducts();
        //    return Ok(products); // Returns a 200 status code with all products
        //}


        [HttpGet("GetLabourForWorkOrder")]
        public async Task<IActionResult> GetLabourForWorkorder(Int32 sheetID)
        {
            try
            {
                //var vehicleData = await _samsaraApiService.GetVehicleByID("281474986627612");
                List<LabourLine> labourDetails = await _labourService.GetLabourByWorkOrder(sheetID);

                if (labourDetails == null || labourDetails.Count == 0)
                {
                    return NotFound("No labour found."); // Returns 404 if no inventory parts are found
                }


                return Ok(labourDetails); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("GetLabourByID")]
        public async Task<IActionResult> GetLabourByID(Int32 labourID)
        {
            try
            {
                LabourLine labourData = await _labourService.GetLabourById(labourID);
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
