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

        public InventoryController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
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
    }
}
