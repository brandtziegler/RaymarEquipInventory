using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InventoryController : Controller
    {
        private readonly IInventoryService _inventoryService;
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;

        public InventoryController(IInventoryService inventoryService, IQuickBooksConnectionService quickBooksConnectionService)
        {
            _inventoryService = inventoryService;
            _quickBooksConnectionService = quickBooksConnectionService; 
        }

        // Dummy endpoint for getting a product by ID
        [HttpGet("{id}")]
        public IActionResult GetProductById(int id)
        {
            var product = _inventoryService.GetProductById(id);
            return Ok(product); // Returns a 200 status code with product details
        }

        // Dummy endpoint for getting all products
        [HttpGet]
        public IActionResult GetAllProducts()
        {
            var products = _inventoryService.GetAllProducts();
            return Ok(products); // Returns a 200 status code with all products
        }

        [HttpPost("UpdateInventory")]
        public IActionResult UpdateInventory([FromBody] List<InventoryData> inventoryDataList)
        {
            if (inventoryDataList == null || inventoryDataList.Count == 0)
            {
                return BadRequest("No inventory data provided.");
            }

            // This is where you would loop through the list and update the database
            // For now, just pretending we're doing something useful
            foreach (var item in inventoryDataList)
            {
                // Logic to update the database would go here
                // For now, just a placeholder
                var message = $"Updating inventory item: {item.ItemName}, ID: {item.InventoryId}";
                // Log or output the message
                Console.WriteLine(message);
            }

            return Ok("Inventory data processed successfully.");
        }

        [HttpGet("GrabInventory")]
        public IActionResult GrabInventory()
        {
            try
            {
                List<InventoryData> inventoryParts = _inventoryService.GetInventoryPartsFromQuickBooks();



                if (inventoryParts == null || inventoryParts.Count == 0)
                {
                    return NotFound("No inventory parts found.");
                }


                return Ok(inventoryParts); // Returns a 200 status code with inventory data
            }
            catch (Exception ex)
            {
                // We're catching the exception like it's a wild steer in the middle of a stampede.
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
