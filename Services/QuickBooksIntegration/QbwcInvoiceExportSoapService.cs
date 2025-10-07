using System;
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
        private const int TEST_INVOICE_ID = 9;

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
            // Use a distinct export user if desired; falls back to options/defaults
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

            // second element "" = use currently-open company file
            return new[] { ticket, "" };
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

            // Remove UTF-8 BOM or stray zero-width characters that can sneak in
            xml = xml.TrimStart('\uFEFF', '\u200B', '\u0000');

            // Remove all leading whitespace before the first '<'
            var first = xml.IndexOf('<');
            return first > 0 ? xml.Substring(first) : xml;
        }

        // ================== Export Flow ==================

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

            // Bootstrap this session with a single InvoiceAdd
            if (string.IsNullOrEmpty(iter.LastRequestType))
            {
                iter.LastRequestType = "InvoiceAddPending";
                _session.SetIterator(runId, iter);
            }

            if (string.Equals(iter.LastRequestType, "InvoiceAddPending", StringComparison.Ordinal))
            {
                try
                {
                    var payload = _invoiceExport.BuildInvoiceAddPayloadAsync(TEST_INVOICE_ID)
                                                .GetAwaiter().GetResult();
                    var xml = _request.BuildInvoiceAdd(payload, qbXMLMajorVers, qbXMLMinorVers, qbXMLCountry);

                    // 🚿 Clean the XML to remove BOM/whitespace/hidden chars
                    xml = CleanForQuickBooks(xml);

                    // Optional: verify and persist for troubleshooting // let's get rid of these stupid errors.
                    var bytes = new UTF8Encoding(false).GetBytes(xml); // no BOM
                    Directory.CreateDirectory(@"C:\Temp");
                    File.WriteAllBytes(@"C:\Temp\InvoiceAddSent.xml", bytes);

                    iter.LastRequestType = "InvoiceAddRq";
                    iter.IteratorId = null;
                    iter.Remaining = 0;
                    _session.SetIterator(runId, iter);

                    // Forensic log of what is actually sent
                    _qbXmlLogger.LogAsync(runId, "req", "sendRequestXML", "InvoiceAddRq",
                        strCompanyFileName, null, TEST_INVOICE_ID, payload.RefNumber,
                        null, null, "sending invoice", xml).GetAwaiter().GetResult();

                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=InvoiceAddRq Ref={payload.RefNumber}").GetAwaiter().GetResult();

                    // ✅ Return a pure string with no BOM or leading whitespace
                    return Encoding.UTF8.GetString(bytes);
                }
                catch (Exception ex)
                {
                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        message: $"InvoiceAdd build failure: {ex.GetType().Name}: {ex.Message}")
                        .GetAwaiter().GetResult();

                    return "";
                }
            }

            // No more work; finish the session
            _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                message: "export-only: no further requests").GetAwaiter().GetResult();
            return "";
        }


        public int receiveResponseXML(string ticket, string response, string hresult, string message)
        {
            if (!_session.TryGetRunId(ticket, out var runId))
                return 0;

            var state = _session.GetIterator(runId);

            // Blank or parser error from QBWC
            if (string.IsNullOrWhiteSpace(response))
            {
                _qbXmlLogger.LogAsync(runId, "resp", "receiveResponseXML", "InvoiceAddRs",
                    null, null, TEST_INVOICE_ID, null, null, hresult, message, response)
                    .GetAwaiter().GetResult();

                _invoiceExport.OnInvoiceExportFailureAsync(TEST_INVOICE_ID, message ?? "XML parse error")
                    .GetAwaiter().GetResult();

                return 100; // done
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

            // Log the raw response with parsed status
            _qbXmlLogger.LogAsync(runId, "resp", "receiveResponseXML", "InvoiceAddRs",
                null, null, TEST_INVOICE_ID, null,
                (int?)parsed?.StatusCode, hresult, (string?)parsed?.StatusMessage, response)
                .GetAwaiter().GetResult();

            // Handle success/failure
            var txnId = (string?)parsed?.InvoiceTxnId;
            var editSeq = (string?)parsed?.InvoiceEditSeq;

            if (!string.IsNullOrWhiteSpace(txnId))
            {
                _invoiceExport.OnInvoiceExportSuccessAsync(TEST_INVOICE_ID, txnId!, editSeq ?? "")
                    .GetAwaiter().GetResult();
            }
            else
            {
                var err = (string?)parsed?.StatusMessage ?? "Unknown error";
                _invoiceExport.OnInvoiceExportFailureAsync(TEST_INVOICE_ID, err)
                    .GetAwaiter().GetResult();
            }

            // One-and-done per session
            state.LastRequestType = "InvoiceExportDone";
            state.IteratorId = null;
            state.Remaining = 0;
            _session.SetIterator(runId, state);
            return 100;
        }
    }
}
