using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Serilog;

namespace RaymarEquipmentInventory.Services
{
    public sealed class QbwcSoapService : IQBWebConnectorSvc
    {
        private readonly IAuditLogger _audit;
        private readonly IQBWCSessionStore _session;
        private readonly IQBWCRequestBuilder _request;
        private readonly IQBWCResponseHandler _response;
        private readonly IInventoryImportService _import;
        private readonly QbwcRequestOptions _opt;

        public QbwcSoapService(
            IAuditLogger audit,
            IQBWCSessionStore session,
            IQBWCRequestBuilder request,
            IQBWCResponseHandler response,
            IInventoryImportService import,
            IOptions<QbwcRequestOptions> opt)
        {
            _audit = audit;
            _session = session;
            _request = request;
            _response = response;
            _import = import;
            _opt = opt.Value ?? new QbwcRequestOptions();
        }

        // ---- SOAP ops ----

        public string[] authenticate(string strUserName, string strPassword)
        {
            // 1) validate creds (must match your QWC's <UserName>)
            const string expectedUser = "raymar-qbwc";
            const string expectedPass = "Thr!ve2025AD";   // <-- pick yours

            if (!string.Equals(strUserName, expectedUser, StringComparison.Ordinal) ||
                !string.Equals(strPassword, expectedPass, StringComparison.Ordinal))
            {
                // tell QBWC "not valid user"
                return new[] { "", "nvu" };
            }

            // 2) start session and return a ticket
            var runId = _session.StartSessionAsync(strUserName ?? "unknown", companyFile: null).GetAwaiter().GetResult();
            var ticket = Guid.NewGuid().ToString("n");
            _session.MapTicketAsync(ticket, runId).GetAwaiter().GetResult();

            _audit.LogMessageAsync(runId, "authenticate", "resp", message: "ok", ct: CancellationToken.None)
                  .GetAwaiter().GetResult();

            // second element "" = use current/open company file
            return new[] { ticket, "" };
        }

        public string clientVersion(string strVersion)
        {
            // Keep it simple for now; can add stricter checks later
            return "";                 // <-- must be empty string
        }

        public string serverVersion()
        {
            return "TaskFuel QBWC Bridge v0.1";
        }

        public string getLastError(string ticket)
        {
            if (_session.TryGetRunId(ticket, out var runId))
            {
                _audit.LogMessageAsync(runId, "getLastError", "resp", message: "No error").GetAwaiter().GetResult();
            }
            return "No error";
        }

        public string closeConnection(string ticket)
        {
            if (_session.TryGetRunId(ticket, out var runId))
            {
                _session.EndSessionAsync(runId, null).GetAwaiter().GetResult();
            }
            return "OK";
        }


        public string connectionError(string ticket, string hresult, string message)
        {
            if (_session.TryGetRunId(ticket, out var runId))
            {
                _audit.LogMessageAsync(runId, "connectionError", "resp",
                                       message: $"{hresult}: {message}")
                      .GetAwaiter().GetResult();
            }
            // Returning "DONE" tells QBWC to stop trying this session,
            // or return a company file path to re-try against another file.
            return "DONE";
        }

        public string sendRequestXML(
            string ticket,
            string strHCPResponse,
            string strCompanyFileName,
            string qbXMLCountry,
            int qbXMLMajorVers,
            int qbXMLMinorVers)
        {
            // Make sure we have a run/session
            if (!_session.TryGetRunId(ticket, out var runId))
            {
                runId = _session.StartSessionAsync("unknown", strCompanyFileName).GetAwaiter().GetResult();
                _session.MapTicketAsync(ticket, runId).GetAwaiter().GetResult();
            }

            // Build the next request based on iterator state
            var iter = _session.GetIterator(runId);
            string xml;

            if (_opt.RequestCompanyQueryFirst && string.IsNullOrEmpty(iter.LastRequestType))
            {
                xml = _request.BuildCompanyQuery();
                iter.LastRequestType = "CompanyQueryRq";
            }
            else if (string.IsNullOrEmpty(iter.IteratorId))
            {
                xml = _request.BuildItemInventoryStart(_opt.PageSize, _opt.ActiveOnly, _opt.FromModifiedDateUtc);
                iter.LastRequestType = "ItemInventoryQueryRq";
            }
            else
            {
                xml = _request.BuildItemInventoryContinue(iter.IteratorId!, _opt.PageSize);
                iter.LastRequestType = "ItemInventoryQueryRq";
            }

            _session.SetIterator(runId, iter);

            // Helpful context for troubleshooting (single audit record)
            var typeLabel = string.IsNullOrEmpty(iter.LastRequestType) ? "start" : iter.LastRequestType;
            var iteratorLabel = string.IsNullOrEmpty(iter.IteratorId) ? "<none>" : iter.IteratorId;

            _audit.LogMessageAsync(
                runId,
                "sendRequestXML",
                "req",
                companyFile: strCompanyFileName,
                message:
                    $"type={typeLabel}; iterator={iteratorLabel}; pageSize={_opt.PageSize}; activeOnly={_opt.ActiveOnly}; fromMod={_opt.FromModifiedDateUtc ?? "<null>"}; qbXmlVer={qbXMLMajorVers}.{qbXMLMinorVers}; country={qbXMLCountry}",
                payloadXml: xml
            ).GetAwaiter().GetResult();

            return xml;
        }


        public int receiveResponseXML(string ticket, string response, string hresult, string message)
        {
            // If we can't map the ticket, end the loop.
            if (!_session.TryGetRunId(ticket, out var runId))
                return 0;

            var state = _session.GetIterator(runId);

            // Parse the qbXML into DTOs + iterator info
            var parsed = _response
                .HandleReceiveAsync(runId, response, hresult, message)
                .GetAwaiter()
                .GetResult();

            // If this was the CompanyQueryRs, just log and continue to inventory
            if (state.LastRequestType == "CompanyQueryRq" || (response?.Contains("<CompanyQueryRs") ?? false))
            {
                _audit.LogMessageAsync(
                    runId,
                    "receiveResponseXML",
                    "resp",
                    message: $"companyRs: hresult={hresult ?? "<null>"}; msg={message ?? "<null>"}"
                ).GetAwaiter().GetResult();

                // Reset to let the next sendRequestXML choose ItemInventory START
                state.LastRequestType = null;
                _session.SetIterator(runId, state);
                return 1; // tell QBWC to continue
            }

            // Inventory batch: insert if we actually parsed items
            var itemCount = parsed?.InventoryItems?.Count ?? 0;
            if (itemCount > 0)
            {
                _import.BulkInsertInventoryAsync(runId, parsed.InventoryItems).GetAwaiter().GetResult();
            }

            // Advance iterator state
            state.IteratorId = parsed?.IteratorId;
            state.Remaining = parsed?.IteratorRemaining ?? 0;
            state.LastRequestType = "ItemInventoryQueryRq";
            _session.SetIterator(runId, state);

            // Helpful audit crumb: how many items this batch, how many left, and any QB error text
            _audit.LogMessageAsync(
                runId,
                "receiveResponseXML",
                "resp",
                message: $"items={itemCount}; remaining={state.Remaining}; hresult={hresult ?? "<null>"}; msg={message ?? "<null>"}"
            ).GetAwaiter().GetResult();

            // 100 = continue; 0 = done
            return state.Remaining > 0 ? 1 : 100;
        }


    }
}
