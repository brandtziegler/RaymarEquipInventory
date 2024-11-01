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
        private readonly IDocumentService _documentService;
        public InventoryController(IInventoryService inventoryService, IDocumentService documentService, IQuickBooksConnectionService quickBooksConnectionService)
        {
            _inventoryService = inventoryService;
            _documentService = documentService;
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

        [HttpPost("InsertInventory")]
        public async Task<IActionResult> InsertInventory(IFormFile file, string uploadedBy, string itemName, string mfgPartNumber, string description)
        {


            // Check if no inventory data was provided
            if ( itemName == null)
            {
                return NotFound("Need item name."); // Returns 404 if no inventory parts are found
            }
            var fileExtension = Path.GetExtension(file.FileName)?.ToLower().TrimStart('.');
            if (fileExtension == null) { return BadRequest("Invalid file extension."); }
            var docIsValid = await _documentService.DocTypeIsValid(fileExtension);
            if (!docIsValid)
            {
                return BadRequest("Invalid document type.");
            }
            // Validate required fields in the provided data
            bool isDataValid = !string.IsNullOrEmpty(itemName) && !string.IsNullOrEmpty(description);
            if (!isDataValid)
            {
                return BadRequest("Invalid inventory data provided. ItemName and Description are required.");
            }

            // Attempt to insert or update inventory data
           var invId =  await _inventoryService.InsertInventoryAsync(itemName, description, mfgPartNumber);
           
            if (invId != 0) {
                bool uploadSuccess = await _documentService.UploadPartDocument(file, uploadedBy, invId);
            }

            // Return success message after processing
            return Ok("Inventory data processed successfully.");
        }

        [HttpGet("GrabInventoryAndUpdate")]
        public async Task<IActionResult> GrabInventoryAndUpdate()
        {
            try
            {
                // Await the async method to ensure it completes before moving on
                List<InventoryData> inventoryParts = await _inventoryService.GetInventoryPartsFromQuickBooksAsync(false);

                if (inventoryParts == null || inventoryParts.Count == 0)
                {
                    return NotFound("No inventory parts found."); // Returns 404 if no inventory parts are found
                }
                else
                {
                    await _inventoryService.UpdateOrInsertInventoryAsync(inventoryParts); // Awaits the async method to update or insert inventory data
                }

                return Ok(inventoryParts); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


        [HttpGet("GetInventoryForDropdown")]
        public async Task<IActionResult> GetInventoryForDropdown(string? searchTerm)
        {
            try
            {
                List<InventoryForDropdown> inventoryParts = await _inventoryService.GetDropdownInfo(searchTerm);

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


        [HttpGet("GetAllInventoryItems")]
        public async Task<IActionResult> GetAllInventoryItems()
        {
            try
            {
                List<InventoryForDropdown> inventoryParts = await _inventoryService.GetAllInventoryItems();

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
