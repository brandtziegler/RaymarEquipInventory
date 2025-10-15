using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.Services;
using Serilog;
using System;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvoiceTestController : ControllerBase
    {
        private readonly IInvoiceExportService _invoiceExport;
        private readonly IQBWCRequestBuilder _request;

        public InvoiceTestController(
            IInvoiceExportService invoiceExport,
            IQBWCRequestBuilder request)
        {
            _invoiceExport = invoiceExport;
            _request = request;
        }

        /// <summary>
        /// Step 1: Calls BuildInvoiceAddPayloadAsync directly to verify DB side (EF layer).
        /// </summary>
        [HttpGet("TestPayload")]
        public async Task<IActionResult> TestPayload(int invoiceId = 10)
        {
            try
            {
                var payload = await _invoiceExport.BuildInvoiceAddPayloadAsync(invoiceId);
                if (payload == null)
                    return NotFound($"No payload returned for InvoiceID {invoiceId}");

                return Ok(new
                {
                    message = "Payload built successfully",
                    refNumber = payload.RefNumber,
                    customerListId = payload.CustomerListID,
                    linesCount = payload.Lines?.Count ?? 0
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 BuildInvoiceAddPayloadAsync failed for InvoiceID {InvoiceId}", invoiceId);
                return StatusCode(500, new
                {
                    error = "BuildInvoiceAddPayloadAsync failed",
                    ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }

        /// <summary>
        /// Step 2: Runs both BuildInvoiceAddPayloadAsync and BuildInvoiceAdd.
        /// Verifies end-to-end XML generation.
        /// </summary>
        [HttpGet("TestXml")]
        public async Task<IActionResult> TestXml(int invoiceId = 10)
        {
            try
            {
                var payload = await _invoiceExport.BuildInvoiceAddPayloadAsync(invoiceId);
                if (payload == null)
                    return NotFound($"No payload returned for InvoiceID {invoiceId}");

                var xml = _request.BuildInvoiceAdd(payload, 14, 0, "US");

                return Ok(new
                {
                    message = "XML generated successfully",
                    xmlLength = xml.Length,
                    xmlStart = xml.Substring(0, Math.Min(300, xml.Length)), // preview
                    refNumber = payload.RefNumber
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 BuildInvoiceAdd test failed for InvoiceID {InvoiceId}", invoiceId);
                return StatusCode(500, new
                {
                    error = "XML generation failed",
                    ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }
    }
}
