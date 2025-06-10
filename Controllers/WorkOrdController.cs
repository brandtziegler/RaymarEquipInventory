using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;
using System.Net.Http;
using System.Net.Mail;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkOrdController : Controller
    {
        private readonly IWorkOrderService _workOrderService;
        private readonly ITechnicianService _technicianService;
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDriveUploaderService _driveUploaderService;
        public WorkOrdController(IWorkOrderService workOrderService, 
            IQuickBooksConnectionService quickBooksConnectionService, ITechnicianService technicianService, 
            ISamsaraApiService samsaraApiService, 
            IHttpClientFactory httpClientFactory, IDriveUploaderService driveUploaderService)
        {
            _workOrderService = workOrderService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
            _technicianService = technicianService;
            _httpClientFactory = httpClientFactory;
            _driveUploaderService = driveUploaderService;

        }


        [HttpPost("LaunchWorkOrder")]
        public async Task<IActionResult> LaunchWorkOrder([FromBody] Billing billingDto)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.LaunchWorkOrder(billingDto);

                if (!result)
                {
                    return BadRequest("Unable to launch work order with the provided billing information.");
                }

                return Ok("Work order launched successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error launching work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }


        [HttpGet("ping")]
        public IActionResult Ping() => Ok("Connected");

        [HttpPost("InsertWorkOrder")]
        public async Task<IActionResult> InsertWorkOrder([FromBody] DTOs.WorkOrdSheet workOrdDto)
        {
            try
            {
                var result = await _workOrderService.InsertWorkOrderAsync(workOrdDto);

                if (result == null)
                    return BadRequest("Unable to create new work order.");

                return Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error($"Error inserting W/O: {ex.Message}");
                return StatusCode(500, "Error inserting work order.");
            }
        }

        [HttpPost("UploadAppFiles")]
        public async Task<IActionResult> UploadAppFiles(List<IFormFile> files, [FromQuery] string custPath, [FromQuery] string workOrderId)
        {
            
            await _driveUploaderService.UploadFilesAsync(files, custPath, workOrderId);
            return Ok("Files uploaded");
        }

        [HttpPost("ListPDFFiles")]
        public async Task<IActionResult> ListPDFFiles()
        {
            try
            {
                var fileMetadataList = await _driveUploaderService.ListFileUrlsAsync();
                if (fileMetadataList == null || !fileMetadataList.Any())
                {
                    return NotFound("No files found");
                }
                return Ok(fileMetadataList);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error occurred while listing PDF files");
                return StatusCode(500, "An error occurred while processing your request.");
            }
        }

        [HttpPost("VerifyKey")]
        public IActionResult VerifyKey()
        {
            try
            {
                var lines = _driveUploaderService.VerifyAndSplitPrivateKey();

                if (lines == null || lines.Count == 0)
                {
                    return BadRequest("❌ Private key is missing or improperly formatted.");
                }

                return Ok(new
                {
                    message = "✅ Private key is valid and properly split.",
                    lineCount = lines.Count,
                    lines = lines
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "💥 Unexpected error during private key verification.");
                return StatusCode(500, "Internal Server Error. Check logs for details.");
            }
        }


        [HttpPost("SendWorkOrderEmail")]
        public async Task<IActionResult> SendWorkOrderEmail([FromBody] DTOs.WorkOrdMailContent dto)
        {
            var resendKey = "re_exsqgshN_HidHMnaoQHNwGn7gn6yy6RbW";

            // ✅ Email format sanity check
            try
            {
                var _ = new MailAddress(dto.EmailAddress);
            }
            catch (FormatException)
            {
                return BadRequest("Invalid email format.");
            }

            var email = new
            {
                from = "service@taskfuel.app",
                to = dto.EmailAddress,
                subject = $"Work Order #{dto.WorkOrderNumber} for {dto.CustPath} Uploaded",
                html = $@"
                    <h2>Work Order Synced</h2>
                    <p><strong>Customer Path:</strong> {dto.CustPath}</p>
                    <p><strong>Description:</strong> {dto.WorkDescription}</p>
                    <p><strong>Work Order #{dto.WorkOrderNumber}</strong> is now live in Firebase & Azure SQL.</p>
                    <p>You can view the uploaded files here:<br>
                    <a href='https://drive.google.com/drive/folders/1adqdzJVDVqdMB6_MSuweBYG8nlr4ASVk'>
                        View WO#{dto.WorkOrderNumber} Files on Google Drive for  {dto.CustPath}
                    </a></p>
                    <p><em>Login Email:</em> raymardeveloper@gmail.com<br>
                    <em>Password:</em> TaskFue!202S</p>"
            };

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", resendKey);

                var response = await client.PostAsJsonAsync("https://api.resend.com/emails", email);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("❌ Email failed: " + body);
                    return StatusCode((int)response.StatusCode, body);
                }

                Console.WriteLine("✅ Email sent.");
                return Ok("Email sent.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("🔥 Exception: " + ex.Message);
                return StatusCode(500, "Internal error: " + ex.Message);
            }
        }


        [HttpGet("GetWorkOrder")]
        public async Task<IActionResult> GetWorkOrder(int sheetID)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.GetWorkOrder(sheetID);

                if (result == null)
                {
                    return BadRequest($"Unable to retrieve work order. Sheet ID {sheetID} does not exist.");
                }

                return Ok("Work order retrieved successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving work order Sheet ID {sheetID}:  {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

        [HttpGet("GetWorkOrderBriefDetails")]
        public async Task<IActionResult> GetWorkOrderBriefDetails()
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.GetWorkOrderBriefDetails();

                foreach (var item in result)
                {
                    item.Techs = await _technicianService.GetTechsByWorkOrder(item.SheetID);

                }

                if (result == null)
                {
                    return BadRequest($"Unable to retrieve work orders.");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving work orders:  {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work orders.");
            }
        }

        [HttpPost("RemoveBillFromWorkOrder")]
        public async Task<IActionResult> RemoveBillFromWorkOrder(int billID, int sheetId)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.RemoveBillFromWorkOrder(billID, sheetId);

                if (!result)
                {
                    return BadRequest("Unable to remove bill from work order");
                }

                return Ok("Bill removed from work order successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error removing bill {billID} from work order {sheetId}: {ex.Message}");
                return StatusCode(500, $"An error occurred while removing the bill {billID} from the work order {sheetId}.");
            }
        }

        [HttpPost("RemoveLbrFromWorkOrder")]
        public async Task<IActionResult> RemoveLbrFromWorkOrder(int lbrID)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.RemoveLbrFromWorkOrder(lbrID);

                if (!result)
                {
                    return BadRequest("Unable to remove labour from work order");
                }

                return Ok("Labour removed from work order successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error removing labour {lbrID} from work order: {ex.Message}");
                return StatusCode(500, $"An error occurred while removing the labour line:  {lbrID} from the work order.");
            }
        }

        [HttpPost("AddPartToWorkorder")]
        public async Task<IActionResult> AddPartToWorkorder([FromBody] PartsUsed partDTO)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.AddPartToWorkOrder(partDTO);

                if (!result)
                {
                    return BadRequest("Unable to add part to work order");
                }

                return Ok("Part added to work order successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error launching work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

        [HttpPost("RemovePartFromWorkorder")]
        public async Task<IActionResult> RemovePartFromWorkOrder(int partID, int sheetId)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.RemovePartFromWorkOrder(partID, sheetId);

                if (!result)
                {
                    return BadRequest("Unable to add part to work order");
                }

                return Ok("Part added to work order successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error launching work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

        [HttpPost("AddLbrToWorkOrder")]
        public async Task<IActionResult> AddLbrToWorkOrder([FromBody] LabourLine labourDTO)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.AddLbrToWorkOrder(labourDTO);

                if (!result)
                {
                    return BadRequest("Unable to add part to work order");
                }

                return Ok("Part added to work order successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error launching work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }
        [HttpPost("AddTechToWorkOrder")]
        public async Task<IActionResult> AddTechToWorkOrder(int techID, int sheetID)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.AddTechToWorkOrder(techID, sheetID);

                if (!result)
                {
                    return BadRequest("Unable to add technician to work order");
                }

                return Ok("Technician added successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"Error adding tech ID {techID} to work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

        [HttpPost("DeleteTechFromWorkOrder")]
        public async Task<IActionResult> DeleteTechFromWorkOrder(int techID, int sheetID)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.DeleteTechFromWorkOrder(techID, sheetID);

                if (!result)
                {
                    return BadRequest("Unable to remove technician to work order");
                }

                return Ok("Technician removed successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"Error removing tech ID {techID} from work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

    }
       
}
