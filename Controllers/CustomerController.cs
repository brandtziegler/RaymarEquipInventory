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

        public CustomerController(ICustomerService customerService, IQuickBooksConnectionService quickBooksConnectionService)
        {
            _customerService = customerService;
            _quickBooksConnectionService = quickBooksConnectionService; 
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
                List<CustomerData> inventoryParts = await _customerService.GetCustomersFromQuickBooksAsnyc();

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




    }
}
