using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;
using Hangfire;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportingController : Controller
    {
        private readonly IReportingService _reportingService;
        private readonly IBackgroundJobClient _jobs;

        public ReportingController(
            IReportingService reportingService,
            IBackgroundJobClient jobs)
        {
            _reportingService = reportingService;
            _jobs = jobs;
        }

        /// <summary>
        /// Returns the invoice XLSX file for the given SheetID (summed by default).
        /// Example: GET /api/reporting/invoices/491?summed=true
        /// </summary>
        [HttpGet("invoices/{sheetId:int}")]
        public async Task<IActionResult> GetInvoiceXlsx(int sheetId, [FromQuery] bool summed = true, CancellationToken ct = default)
        {
            try
            {
                var (fileName, xlsx) = await _reportingService.GetInvoiceXlsxPackageAsync(sheetId, summed, ct);
                var bytes = xlsx.ToArray();

                const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                return File(bytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ GetInvoiceXlsx failed for SheetID {SheetId} (summed={Summed})", sheetId, summed);
                return StatusCode(500, $"Failed to build invoice XLSX for SheetID {sheetId}.");
            }
        }

        /// <summary>
        /// Sends the invoice XLSX via email (Resend) for the given SheetID.
        /// Enqueues a background job and returns 202 with the Hangfire job id.
        /// Example: POST /api/reporting/invoices/491/send?summed=true
        /// </summary>
        [HttpPost("invoices/{sheetId:int}/send")]
        public IActionResult SendInvoiceXlsx(int sheetId, [FromQuery] bool summed = true)
        {
            try
            {
                var jobId = _jobs.Enqueue(() =>
                    _reportingService.SendInvoiceXlsxAsync(sheetId, summed, CancellationToken.None));

                return Accepted(new
                {
                    jobId,
                    message = $"Queued invoice email for SheetID {sheetId} (summed={summed})."
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ SendInvoiceXlsx enqueue failed for SheetID {SheetId} (summed={Summed})", sheetId, summed);
                return StatusCode(500, $"Failed to enqueue invoice email for SheetID {sheetId}.");
            }
        }



        // In your ReportingController (same DI pattern as your other actions)

        /// <summary>
        /// Imports Parts from an uploaded XLSX (Sheet1 header must match:
        /// Item, Description, Preferred Vendor, U/M, Price).
        /// Example: POST /api/reporting/parts/importxlsx  (multipart/form-data with file field "file")
        /// </summary>
        [HttpPost("ImportPartsXlsx")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportPartsXlsx([FromForm] FileUploadDto input, CancellationToken ct)
        {
            if (input.File == null || input.File.Length == 0)
                return BadRequest("No file uploaded.");

            using var stream = input.File.OpenReadStream();
            var result = await _reportingService.ImportPartsAsync(stream, ct);

            return Ok(new
            {
                file = input.File.FileName,
                result.Inserted,
                result.Updated,
                result.Reactivated,
                result.MarkedInactive,
                result.Rejected,
                result.InsertedSamples,
                result.UpdatedSamples,
                result.ReactivatedSamples,
                result.MarkedInactiveSamples,
                result.Timestamp
            });
        }


        [HttpPost("invoicesiif/{sheetId:int}/send")]
        public IActionResult SendInvoiceIIF(int sheetId, [FromQuery] bool summed = true)
        {
            try
            {
                var jobId = _jobs.Enqueue(() =>
                    _reportingService.SendInvoiceIIFAsync(sheetId, summed, CancellationToken.None));

                return Accepted(new
                {
                    jobId,
                    message = $"Queued invoice email for SheetID {sheetId} (summed={summed})."
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ SendInvoiceXlsx enqueue failed for SheetID {SheetId} (summed={Summed})", sheetId, summed);
                return StatusCode(500, $"Failed to enqueue invoice email for SheetID {sheetId}.");
            }
        }

        /// <summary>
        /// Returns the invoice XLSX file for the given SheetID (summed by default).
        /// Example: GET /api/reporting/invoices/491?summed=true
        /// </summary>
        [HttpGet("invoicesiif/{sheetId:int}")]
        public async Task<IActionResult> GetInvoiceIIF(int sheetId, [FromQuery] bool summed = true, CancellationToken ct = default)
        {
            try
            {
                var (fileName, xlsx) = await _reportingService.GetInvoiceIifPackageAsync(sheetId, summed, ct);
                var bytes = xlsx.ToArray();

                const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                return File(bytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ GetInvoiceIIF failed for SheetID {SheetId} (summed={Summed})", sheetId, summed);
                return StatusCode(500, $"Failed to build invoice IIF for SheetID {sheetId}.");
            }
        }
    }
}
