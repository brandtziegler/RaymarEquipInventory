using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;

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


        // New method to upload a document
        [HttpPost("UploadDocument")]
        public async Task<IActionResult> UploadDocument(IFormFile file, string uploadedBy)
        {
            try
            {
                // Step 1: Validate if file and uploadedBy are present
                if (file == null || string.IsNullOrWhiteSpace(uploadedBy))
                {
                    return BadRequest("File and uploader information must be provided.");
                }

                // Step 2: Get file extension and convert to lowercase
                var fileExtension = Path.GetExtension(file.FileName)?.ToLower().TrimStart('.');
                
                var docIsValid = await _documentService.DocTypeIsValid(fileExtension);


                //// Step 4: Check if the file extension matches any valid document type
                if (!docIsValid)
                {
                    return BadRequest("Invalid document type.");
                }

                //var documentTypeID = validDocumentTypes[fileExtension];

                // Step 5: Get file details
                var fileName = file.FileName;
                var fileType = file.ContentType;
                //var uploadTimestamp = DateTime.Now;
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");  // Replace with the desired time zone
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
                var uploadTimestamp = localTime.ToString("MMMM dd, yyyy hh:mm tt");

                // Step 6: Mock saving file to a blob storage (skip actual saving for now)
                // Simulate file being saved and URL being generated
                var fileUrl = $"https://blobstorage.com/sheet-1234/{fileName}"; // Simulated Blob URL

                // Step 7: Return document details and uploader info
                var documentDetails = new
                {
                    FileName = fileName,
                    FileType = fileType,
                    UploadDate = uploadTimestamp,
                    UploadedBy = uploadedBy,
                    FileUrl = fileUrl, 
                    DateUploaded = uploadTimestamp

                };

                return Ok(documentDetails);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

    }
       
}
