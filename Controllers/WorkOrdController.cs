using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;
using System.Collections.Concurrent;
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

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> FolderLocks = new();

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

        [HttpPost("InsertUpdateWorkOrder")]
        public async Task<IActionResult> InsertUpdateWorkOrder([FromBody] DTOs.WorkOrdSheet workOrdDto)
        {

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                WorkOrderInsertResult? result;

                if (workOrdDto.RemoteSheetId == 0)
                {
                    result = await _workOrderService.InsertWorkOrderAsync(workOrdDto);
                    if (result == null)
                        return StatusCode(500, "InsertWorkOrder returned null (see logs).");
                }
                else
                {
                    result = await _workOrderService.UpdateWorkOrderAsync(workOrdDto);
                    if (result == null)
                        return StatusCode(500, "UpdateWorkOrderAsync returned null (see logs).");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "InsertWorkOrder failed");
                return BadRequest(ex.ToString());
            }
        }

        [HttpPost("ClearAppFiles")]
        public async Task<IActionResult> ClearAppFiles([FromQuery] string custPath, [FromQuery] string workOrderId)
        {
            try
            {
                await _driveUploaderService.ClearImageFolderAsync(custPath, workOrderId);
                return Ok(new { message = "Folder cleared." });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Failed to clear image folder.");
                return StatusCode(500, new { message = "Clear failed", error = ex.Message });
            }
        }

        [HttpPost("PrepareDriveFolders")]
        public async Task<IActionResult> PrepareDriveFolders(
            [FromQuery] string custPath,
            [FromQuery] string workOrderId,
            [FromQuery] int sheetID)
        {
            if (string.IsNullOrWhiteSpace(custPath) || string.IsNullOrWhiteSpace(workOrderId))
            {
                return BadRequest(new { message = "custPath and workOrderId are required." });
            }

            var key = custPath.Split('>').First().Trim().ToLower(); // lock on root customer only
            var semaphore = FolderLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

            GoogleDriveFolderDTO folderResult = new();

            try
            {
                await semaphore.WaitAsync();

                folderResult = await _driveUploaderService.PrepareGoogleDriveFoldersAsync(custPath, workOrderId);
                folderResult.SheetID = sheetID;

                if (folderResult.stupidLogErrors.Any())
                {
                    return StatusCode(500, folderResult); // Return logs for diagnostic failure
                }

                return Ok(folderResult);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Controller-level failure for {custPath} / {workOrderId}");

                folderResult.SheetID = sheetID;
                folderResult.stupidLogErrors.Add($"🔥 Controller exception: {ex.Message}");

                return StatusCode(500, folderResult); // Still return DTO with logs
            }
            finally
            {
                semaphore.Release();
            }
        }



        [HttpPost("UploadAppFiles")]
        public async Task<IActionResult> UploadAppFiles(
            List<IFormFile> files,
            [FromQuery] string workOrderId,
            [FromQuery] string workOrderFolderId,
            [FromQuery] string pdfFolderId,
            [FromQuery] string imagesFolderId)
        {
            List<FileUpload> uploads;

            try
            {
                uploads = await _driveUploaderService.UploadFilesAsync(
                    files,
                    workOrderId,
                    workOrderFolderId,
                    pdfFolderId,
                    imagesFolderId
                );

                foreach (var upload in uploads)
                {
                    try
                    {
                        if (upload.Extension is ".jpg" or ".jpeg" or ".png")
                        {
                            await _driveUploaderService.UpdateFileUrlInPartsDocumentAsync(
                                upload.FileName,
                                upload.ResponseBodyId,
                                upload.Extension,
                                upload.WorkOrderId
                            );
                            upload.stupidLogErrors.Add("✅ DB update: parts document link set.");
                        }
                        else if (upload.Extension is ".pdf")
                        {
                            await _driveUploaderService.UpdateFileUrlInPDFDocumentAsync(new PDFUploadRequest
                            {
                                FileName = upload.FileName,
                                FileId = upload.ResponseBodyId,
                                WorkOrderId = upload.WorkOrderId,
                                UploadedBy = "iPad App",
                                Description = string.Empty
                            });
                            upload.stupidLogErrors.Add("✅ DB update: PDF document link set.");
                        }
                        else
                        {
                            var msg = $"⚠️ Skipping unsupported file type: {upload.FileName} ({upload.Extension})";
                            Log.Warning(msg);
                            upload.stupidLogErrors.Add(msg);
                        }
                    }
                    catch (Exception dbEx)
                    {
                        var errMsg = $"❌ Failed DB update for {upload.FileName}: {dbEx.Message}";
                        Log.Error(dbEx, errMsg);
                        upload.stupidLogErrors.Add(errMsg);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 Entire UploadAppFiles operation failed");
                return StatusCode(500, new { message = "Upload failed", error = ex.Message });
            }

            return Ok(uploads);
        }
        [HttpPost("ListPDFFiles")]
        public async Task<IActionResult> ListPDFFiles(int sheetId)
        {
            try
            {
                var fileMetadataList = await _driveUploaderService.ListFileUrlsAsync(sheetId);
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
                    <a href='https://drive.google.com/drive/folders/1Hd4opYT_bV_JbQzMoEzDMxPdWGnjFXTl'>
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

        [HttpGet("GetLabourLines")]
        public async Task<IActionResult> GetLabourLines(int sheetID)
        {
            try
            {
                var result = await _workOrderService.GetLabourLines(sheetID);

                if (result == null)
                {
                    return NotFound($"No labour found for Sheet ID {sheetID}.");
                }

                return Ok(result); // ✅ Return the full JSON list
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving labour used for Sheet ID {sheetID}: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving labour.");
            }
        }


        [HttpGet("GetFees")]
        public async Task<IActionResult> GetFees(int sheetID)
        {
            try
            {
                var result = await _workOrderService.GetFees(sheetID);

                if (result == null)
                {
                    return NotFound($"No parts used found for Sheet ID {sheetID}.");
                }

                return Ok(result); // ✅ Return the full JSON list
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving parts used for Sheet ID {sheetID}: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving parts used.");
            }
        }

        [HttpGet("GetMileage")]
        public async Task<IActionResult> GetMileage(int sheetID)
        {
            try
            {
                var result = await _workOrderService.GetMileage(sheetID);

                if (result == null)
                {
                    return NotFound($"No mileage used found for Sheet ID {sheetID}.");
                }

                return Ok(result); // ✅ Return the full JSON list
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving mileage used for Sheet ID {sheetID}: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving mileage.");
            }
        }

        [HttpGet("GetPartsUsed")]
        public async Task<IActionResult> GetPartsUsed(int sheetID)
        {
            try
            {
                var result = await _workOrderService.GetPartsUsed(sheetID);

                if (result == null)
                {
                    return NotFound($"No parts used found for Sheet ID {sheetID}.");
                }

                return Ok(result); // ✅ Return the full JSON list
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving parts used for Sheet ID {sheetID}: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving parts used.");
            }
        }

        [HttpGet("DownloadWorkOrderHours")]
        public async Task<IActionResult> DownloadWorkOrderHours(
        [FromQuery] int sheetId,
        [FromQuery] int? technicianId = null,
        [FromQuery] int? labourTypeId = null)
        {
            try
            {
                var result = await _workOrderService.GetLabourLines(sheetId, technicianId, labourTypeId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                await _workOrderService.LogFailedSync(sheetId, ex.Message);
                return StatusCode(500, "Could not download work order.");
            }
        }

        [HttpGet("DownloadWorkOrder/{sheetId}")]
        public async Task<IActionResult> DownloadWorkOrder(int sheetId)
        {
            try
            {
                var workOrdNumber = await _workOrderService.GetWorkOrderNumber(sheetId);
                var result = new WorkOrderDetails
                {
                    WorkOrderNumber = workOrdNumber,
                    SheetId = sheetId,
                    TechWorkOrder = await _technicianService.GetTechsByWorkOrder(sheetId),
                    Billing = await _workOrderService.GetBillingMin(sheetId) ?? new BillingMin(),
                    Parts = await _workOrderService.GetPartsUsed(sheetId),
                    Fees = await _workOrderService.GetFees(sheetId),
                    MileageAndTime = await _workOrderService.GetMileage(sheetId),
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                await _workOrderService.LogFailedSync(sheetId, ex.Message);
                return StatusCode(500, "Could not download work order.");
            }
        }


        [HttpGet("GetBilling")]
        public async Task<IActionResult> GetBilling(int sheetID)
        {
            try
            {
                var result = await _workOrderService.GetBillingMin(sheetID);

                if (result == null)
                {
                    return NotFound($"No billing found for Sheet ID {sheetID}.");
                }

                return Ok(result); // ✅ Return the full JSON list
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving billing for Sheet ID {sheetID}: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving billing.");
            }
        }


        [HttpGet("GetWorkOrderCards")]
        public async Task<IActionResult> GetWorkOrderCards(
            [FromQuery] DateTime? dateUploadedStart,
            [FromQuery] DateTime? dateUploadedEnd,
            [FromQuery] DateTime? dateTimeCompletedStart,
            [FromQuery] DateTime? dateTimeCompletedEnd,
            [FromQuery] int technicianID,
            [FromQuery] string workOrderStatus = "COMPLETE",
            [FromQuery] int? customerId = null)
        {
            try
            {
                var result = await _workOrderService.GetWorkOrderCardsAsync(
                    dateUploadedStart,
                    dateUploadedEnd,
                    dateTimeCompletedStart,
                    dateTimeCompletedEnd,
                    technicianID, 
                    workOrderStatus,
                    customerId
                );

                // ✅ Return 200 even if the list is empty
                return Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving work order cards: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving work order cards.");
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

        [HttpPost("UpdateWOStatus")]
        public async Task<IActionResult> UpdateWOStatus(int sheetId, string workOrderStatus, string deviceId)
        {
            try
            {
                await _workOrderService.UpdateWOStatus(sheetId, workOrderStatus, deviceId);

                return Ok("Work order status updated.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating WO status to {workOrderStatus} for SheetID {sheetId}: {ex.Message}");
                return StatusCode(500, "An error occurred while updating the work order status.");
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
