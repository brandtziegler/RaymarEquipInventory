using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentController : Controller
    {
        private readonly IDocumentService _documentService;

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;

        public DocumentController(IDocumentService documentService, IQuickBooksConnectionService quickBooksConnectionService, ISamsaraApiService samsaraApiService)
        {
            _documentService = documentService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
        }

        [HttpGet("GetDocsForWorkOrder")]
        public async Task<IActionResult> GetDocsForWorkOrder(int sheetID)
        {
            try
            {
                //var vehicleData = await _samsaraApiService.GetVehicleByID("281474986627612");
                List<DTOs.RetrieveDocument> retrievedDocs = await _documentService.GetDocumentsByWorkOrder(sheetID);

                if (retrievedDocs == null )
                {
                    return NotFound("No documents found."); // Returns 404 if no inventory parts are found
                }


                return Ok(retrievedDocs); // Returns a 200 status code with the inventory data
            }
            catch (Exception ex)
            {
                // Catching the exception and returning a 500 error with the message
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpDelete("DeleteDocument/{docID}")]
        public async Task<IActionResult> DeleteDocument(int docID)
        {
            try
            {
                // Step 1: Use the service to delete the document
                bool deleteResult = await _documentService.DeleteDocumentById(docID);

                if (!deleteResult)
                {
                    // Return 404 if document not found or could not be deleted
                    return NotFound("Document not found or could not be deleted.");
                }

                // If deletion was successful, return 200 OK
                return Ok("Document deleted successfully.");
            }
            catch (Exception ex)
            {
                // Log the error and return 500 Internal Server Error
                Log.Error($"Error deleting document with ID {docID}: {ex.Message}");
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpGet("GetDocumentInfoByID")]
        public async Task<IActionResult> GetDocumentInfoByID(int docID)
        {
            try
            {
                // Step 1: Fetch the document metadata from the service
                var document = await _documentService.GetDocumentByID(docID);

                if (document == null)
                {
                    return NotFound("Document not found.");
                }

                // Step 2: Get the content of the document from Azure Blob Storage
                var (fileStream, contentType, fileName) = await _documentService.GetDocumentContent(document.FileURL, document.FileType);

                if (fileStream == null)
                {
                    return NotFound("Unable to retrieve the document content.");
                }

                return File(fileStream, contentType, fileName);

            }
            catch (Exception ex)
            {
                // Log the exception and return a 500 error
                Log.Error($"Error accessing document: {ex.Message}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }



        // New method to upload a document
        [HttpPost("UploadDocument")]
        public async Task<IActionResult> UploadDocument(IFormFile file, string uploadedBy, int workOrderNumber)
        {
            try
            {
                // Step 1: Validate if file and uploadedBy are present
                if (file == null || string.IsNullOrWhiteSpace(uploadedBy))
                {
                    return BadRequest("File and uploader information must be provided.");
                }

                // Step 2: Get file extension and validate the document type
                var fileExtension = Path.GetExtension(file.FileName)?.ToLower().TrimStart('.');
                if (fileExtension == null ) { return BadRequest("Invalid file extension."); }
                var docIsValid = await _documentService.DocTypeIsValid(fileExtension);

                if (!docIsValid)
                {
                    return BadRequest("Invalid document type.");
                }

                // Step 3: Call the UploadDoc method in your DocumentService
                bool uploadSuccess = await _documentService.UploadDoc(file, uploadedBy, workOrderNumber); // Await here

                if (!uploadSuccess)
                {
                    return StatusCode(500, "Error uploading the document.");
                }

                return Ok("Document uploaded successfully.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

    }
       
}
