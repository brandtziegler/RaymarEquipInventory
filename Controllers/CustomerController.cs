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


        [HttpGet("GetCustomersForDropdown")]
        public async Task<IActionResult> GetCustomersForDropdown()
        {
            try
            {
                //var vehicleData = await _samsaraApiService.GetVehicleByID("281474986627612");
                List<CustomerData> inventoryParts = await _customerService.GetCustomersFromQuickBooksAsnyc(true);

                if (inventoryParts == null || inventoryParts.Count == 0)
                {
                    return NotFound("No inventory parts found."); // Returns 404 if no inventory parts are found
                }


                return Ok(inventoryParts); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("GetCustomerByID")]
        public async Task<IActionResult> GetCustomerByID(Int32 custID)
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
