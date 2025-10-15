using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    /// <summary>
    /// Invoice-only QBWC SOAP service: pushes ONE InvoiceAdd per session, then exits.
    /// Register as a second QWC app (e.g., "Raymar Invoice Export").
    /// </summary>
    public sealed class QbwcInvoiceExportSoapService : IQBWebConnectorSvc
    {
        private readonly IAuditLogger _audit;
        private readonly IQBWCSessionStore _session;
        private readonly IQBWCRequestBuilder _request;
        private readonly IQBWCResponseHandler _response;
        private readonly IInvoiceExportService _invoiceExport;
        private readonly IQbXmlLogger _qbXmlLogger;
        private readonly QbwcRequestOptions _opt;

        // Hardcoded single-invoice test; replace with queue later
        private const int TEST_INVOICE_ID = 10;

        public QbwcInvoiceExportSoapService(
            IAuditLogger audit,
            IQBWCSessionStore session,
            IQBWCRequestBuilder request,
            IQBWCResponseHandler response,
            IInvoiceExportService invoiceExport,
            IQbXmlLogger qbXmlLogger,
            IOptions<QbwcRequestOptions> opt)
        {
            _audit = audit;
            _session = session;
            _request = request;
            _response = response;
            _invoiceExport = invoiceExport;
            _qbXmlLogger = qbXmlLogger;
            _opt = opt.Value ?? new QbwcRequestOptions();
        }

        // ================== SOAP Required Methods ==================

        public string[] authenticate(string strUserName, string strPassword)
        {
            const string expectedUser = "raymar-qbwc";
            const string expectedPass = "Thr!ve2025AD";

            if (!string.Equals(strUserName, expectedUser, StringComparison.Ordinal) ||
                !string.Equals(strPassword, expectedPass, StringComparison.Ordinal))
            {
                return new[] { "", "nvu" };
            }

            var runId = _session.StartSessionAsync(strUserName, companyFile: null).GetAwaiter().GetResult();
            var ticket = Guid.NewGuid().ToString("n");
            _session.MapTicketAsync(ticket, runId).GetAwaiter().GetResult();

            _audit.LogMessageAsync(runId, "authenticate", "resp", message: "ok", ct: CancellationToken.None)
                  .GetAwaiter().GetResult();

            return new[] { ticket, "" }; // use currently-open company file
        }

        public string clientVersion(string strVersion) => "";
        public string serverVersion() => "TaskFuel QBWC Invoice Export v0.1";

        public string getLastError(string ticket)
        {
            if (_session.TryGetRunId(ticket, out var runId))
                _audit.LogMessageAsync(runId, "getLastError", "resp", message: "No error").GetAwaiter().GetResult();
            return "No error";
        }

        public string connectionError(string ticket, string hresult, string message)
        {
            if (_session.TryGetRunId(ticket, out var runId))
                _audit.LogMessageAsync(runId, "connectionError", "resp", message: $"{hresult}: {message}").GetAwaiter().GetResult();
            return "DONE"; // stop this session
        }

        public string closeConnection(string ticket)
        {
            if (_session.TryGetRunId(ticket, out var runId))
                _session.EndSessionAsync(runId, null).GetAwaiter().GetResult();
            return "OK";
        }

        private static string CleanForQuickBooks(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return xml;
            xml = xml.TrimStart('\uFEFF', '\u200B', '\u0000');
            var first = xml.IndexOf('<');
            return first > 0 ? xml.Substring(first) : xml;
        }

        // ================== Export Flow ==================///make sure we write to the right temp directory. 

        public string sendRequestXML(
            string ticket,
            string strHCPResponse,
            string strCompanyFileName,
            string qbXMLCountry,
            int qbXMLMajorVers,
            int qbXMLMinorVers)
        {
            if (!_session.TryGetRunId(ticket, out var runId))
            {
                runId = _session.StartSessionAsync("unknown", strCompanyFileName).GetAwaiter().GetResult();
                _session.MapTicketAsync(ticket, runId).GetAwaiter().GetResult();
            }

            var iter = _session.GetIterator(runId);

            if (string.IsNullOrEmpty(iter.LastRequestType))
            {
                iter.LastRequestType = "InvoiceAddPending";
                _session.SetIterator(runId, iter);
            }

            if (!string.Equals(iter.LastRequestType, "InvoiceAddPending", StringComparison.Ordinal))
                return string.Empty;

            try
            {
                // ---------------------------------------------------------------------
                // Build payload
                // ---------------------------------------------------------------------
                var payload = _invoiceExport.BuildInvoiceAddPayloadAsync(TEST_INVOICE_ID)
                                            .GetAwaiter().GetResult();

                // ---------------------------------------------------------------------
                // Strip any existing header completely
                // ---------------------------------------------------------------------
                var body = _request.BuildInvoiceAdd(payload, 14, 0, "US");
                int qbxmlIndex = body.IndexOf("<QBXML", StringComparison.OrdinalIgnoreCase);
                if (qbxmlIndex > 0)
                    body = body.Substring(qbxmlIndex);

                // ---------------------------------------------------------------------
                // Hard-coded header – absolutely no line breaks or spaces
                // ---------------------------------------------------------------------
                const string header = "<?xml version=\"1.0\"?><?qbxml version=\"14.0\"?>";
                string xml = string.Concat(header, body);

                // ---------------------------------------------------------------------
                // Write UTF-8 without BOM for inspection
                // ---------------------------------------------------------------------
                var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                var bytes = utf8NoBom.GetBytes(xml);

                var tempDir = Path.GetTempPath();
                Directory.CreateDirectory(tempDir);
                var tempFile = Path.Combine(tempDir, $"InvoiceAddSent_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xml");
                File.WriteAllBytes(tempFile, bytes);
                var xmlPreview = xml.Length > 8000 ? xml.Substring(0, 8000) + "\n...[truncated]..." : xml;
                _qbXmlLogger.LogAsync(runId, "debug", "sendRequestXML",
                    "InvoiceXML",
                    strCompanyFileName, null, TEST_INVOICE_ID, payload.RefNumber,
                    null, null, xmlPreview, null)
                    .GetAwaiter().GetResult();

                // ---------------------------------------------------------------------
                // Log the temp file path for completeness
                // ---------------------------------------------------------------------
                _qbXmlLogger.LogAsync(runId, "info", "sendRequestXML", "TempFilePath",
                    strCompanyFileName, null, TEST_INVOICE_ID, payload.RefNumber,
                    null, null, $"QuickBooks XML written to: {tempFile}", null)
                    .GetAwaiter().GetResult();

                // ---------------------------------------------------------------------
                // Log + iterator update
                // ---------------------------------------------------------------------
                iter.LastRequestType = "InvoiceAddRq";
                iter.IteratorId = null;
                iter.Remaining = 0;
                _session.SetIterator(runId, iter);

                _qbXmlLogger.LogAsync(runId, "req", "sendRequestXML", "InvoiceAddRq",
                    strCompanyFileName, null, TEST_INVOICE_ID, payload.RefNumber,
                    null, null, "sending invoice", xml)
                    .GetAwaiter().GetResult();

                _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                    companyFile: strCompanyFileName,
                    message: $"type=InvoiceAddRq Ref={payload.RefNumber}")
                    .GetAwaiter().GetResult();

                // ---------------------------------------------------------------------
                // Return clean XML (UTF-8 text, no re-encoding)
                // ---------------------------------------------------------------------

                // 🔹 Optional summary log for quick visibility in QbwcXmlLog
                _qbXmlLogger.LogAsync(runId, "summary", "sendRequestXML", "InvoiceAddRq",
                    strCompanyFileName, null, TEST_INVOICE_ID, payload.RefNumber,
                    null, null, $"Attempted invoice export for RefNumber={payload.RefNumber}", null)
                    .GetAwaiter().GetResult();

                // 🔹 Return the XML that QuickBooks will receive
                return CleanForQuickBooks(xml);
            }
            catch (Exception ex)
            {
                _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                    message: $"InvoiceAdd build failure: {ex.GetType().Name}: {ex.Message}")
                    .GetAwaiter().GetResult();
                return string.Empty;
            }
        }



        public int receiveResponseXML(string ticket, string response, string hresult, string message)
        {
            if (!_session.TryGetRunId(ticket, out var runId))
                return 0;

            var state = _session.GetIterator(runId);

            if (string.IsNullOrWhiteSpace(response))
            {
                _qbXmlLogger.LogAsync(runId, "resp", "receiveResponseXML", "InvoiceAddRs",
                    null, null, TEST_INVOICE_ID, null, null, hresult, message, response)
                    .GetAwaiter().GetResult();

                _invoiceExport.OnInvoiceExportFailureAsync(TEST_INVOICE_ID, message ?? "XML parse error")
                    .GetAwaiter().GetResult();
                return 100;
            }

            dynamic? parsed = null;
            try
            {
                parsed = _response.HandleReceiveAsync(runId, response, hresult, message).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _qbXmlLogger.LogAsync(runId, "resp", "receiveResponseXML", "InvoiceAddRs",
                    null, null, TEST_INVOICE_ID, null, null, hresult, $"parser exception: {ex.Message}", response)
                    .GetAwaiter().GetResult();

                _invoiceExport.OnInvoiceExportFailureAsync(TEST_INVOICE_ID, ex.Message)
                    .GetAwaiter().GetResult();
                return 100;
            }

            _qbXmlLogger.LogAsync(runId, "resp", "receiveResponseXML", "InvoiceAddRs",
                null, null, TEST_INVOICE_ID, null,
                (int?)parsed?.StatusCode, hresult, (string?)parsed?.StatusMessage, response)
                .GetAwaiter().GetResult();

            var txnId = (string?)parsed?.InvoiceTxnId;
            var editSeq = (string?)parsed?.InvoiceEditSeq;

            if (!string.IsNullOrWhiteSpace(txnId))
            {
                _invoiceExport.OnInvoiceExportSuccessAsync(TEST_INVOICE_ID, txnId!, editSeq ?? "")
                    .GetAwaiter().GetResult();

                // 🧹 Clean up any old XML temp files older than an hour
                try
                {
                    var tempDir = Path.GetTempPath();
                    foreach (var file in Directory.GetFiles(tempDir, "InvoiceAddSent_*.xml"))
                    {
                        var age = DateTime.UtcNow - File.GetCreationTimeUtc(file);
                        if (age > TimeSpan.FromHours(1))
                            File.Delete(file);
                    }
                }
                catch (Exception cleanupEx)
                {
                    _qbXmlLogger.LogAsync(runId, "warn", "receiveResponseXML", "Cleanup",
                        null, null, TEST_INVOICE_ID, null, null, null,
                        $"Could not clean temp files: {cleanupEx.Message}", null)
                        .GetAwaiter().GetResult();
                }
            }
            else
            {
                var err = (string?)parsed?.StatusMessage ?? "Unknown error";
                _invoiceExport.OnInvoiceExportFailureAsync(TEST_INVOICE_ID, err)
                    .GetAwaiter().GetResult();
            }

            state.LastRequestType = "InvoiceExportDone";
            state.IteratorId = null;
            state.Remaining = 0;
            _session.SetIterator(runId, state);
            return 100;
        }
    }
}
