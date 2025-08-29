using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CustomerController : Controller
    {
        private readonly ICustomerService _customerService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;

        public CustomerController(ICustomerService customerService, IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService)
        {
            _customerService = customerService;
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


        [HttpGet("Watermark")]
        public async Task<IActionResult> GetWatermark(CancellationToken ct)
        {
            var wm = await _customerService.GetWatermarkAsync(ct);
            return Ok(wm);
        }


        // GET /api/Customer/GetCusts?since=...&limit=...
        [HttpGet("GetCusts")]
        public async Task<IActionResult> GetCusts([FromQuery] string? since = null, [FromQuery] int limit = 500, CancellationToken ct = default)
        {
            DateTime? sinceUtc = null;
            if (!string.IsNullOrWhiteSpace(since))
            {
                if (DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    sinceUtc = parsed.ToUniversalTime();
            }

            limit = Math.Clamp(limit, 1, 2000);

            var res = await _customerService.GetCustomerChangesAsync(sinceUtc, limit, ct);
            return Ok(res);
        }

        [HttpGet("GetCustomersForDropdown")]
        public async Task<IActionResult> GetCustomersForDropdown()
        {
            try
            {
                //var vehicleData = await _samsaraApiService.GetVehicleByID("281474986627612");
                List<CustomerData> customers = await _customerService.GetCustomersFromQuickBooksAsnyc(true);

                if (customers == null || customers.Count == 0)
                {
                    return NotFound("No customers found."); // Returns 404 if no inventory parts are found
                }


                return Ok(customers); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("GetCustomerByID")]
        public async Task<IActionResult> GetCustomerByID(int custID)
        {
            try
            {
                CustomerData customerData = await _customerService.GetCustomerByID(custID);
                if (customerData == null)
                {
                    return NotFound("No customer with this ID found."); // Returns 404 if no customer is found
                }
                return Ok(customerData);
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");

            }
        }
    }
       
}
