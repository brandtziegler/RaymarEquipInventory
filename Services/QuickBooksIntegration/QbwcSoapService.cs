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
            // Ensure run/session
            if (!_session.TryGetRunId(ticket, out var runId))
            {
                runId = _session.StartSessionAsync("unknown", strCompanyFileName).GetAwaiter().GetResult();
                _session.MapTicketAsync(ticket, runId).GetAwaiter().GetResult();
            }

            var iter = _session.GetIterator(runId);
            string xml;

            // ---- PHASE 1: Company (optional, once) ----
            if (_opt.RequestCompanyQueryFirst &&
                string.IsNullOrEmpty(iter.LastRequestType) &&
                string.IsNullOrEmpty(iter.IteratorId))
            {
                xml = _request.BuildCompanyQuery();
                iter.LastRequestType = "CompanyQueryRq";
                _session.SetIterator(runId, iter);

                _audit.LogMessageAsync(
                    runId, "sendRequestXML", "req",
                    companyFile: strCompanyFileName,
                    message: $"type=CompanyQueryRq; iterator=<none>; qbXmlVer={qbXMLMajorVers}.{qbXMLMinorVers}; country={qbXMLCountry}",
                    payloadXml: xml
                ).GetAwaiter().GetResult();

                return xml;
            }

            // ---- PHASE 2: Inventory START (forced unfiltered first page after Company) ----
            if (string.Equals(iter.LastRequestType, "CompanyDone", StringComparison.Ordinal))
            {
                const int firstPage = 1000;

                // Unfiltered START: no ActiveOnly, no FromModified
                xml = _request.BuildItemInventoryStart(
                    pageSize: firstPage,
                    activeOnly: false,
                    fromModifiedIso8601Utc: null
                );

                iter.LastRequestType = "ItemInventoryQueryRq";
                iter.IteratorId = null; // explicit START
                _session.SetIterator(runId, iter);

                _audit.LogMessageAsync(
                    runId, "sendRequestXML", "req",
                    companyFile: strCompanyFileName,
                    message: $"type=ItemInventoryQueryRq(START,unfiltered); iterator=<none>; maxReturned={firstPage}; qbXmlVer={qbXMLMajorVers}.{qbXMLMinorVers}",
                    payloadXml: xml
                ).GetAwaiter().GetResult();

                return xml;
            }

            // ---- PHASE 2 (normal START when no company step) ----
            if (string.IsNullOrEmpty(iter.IteratorId))
            {
                var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;

                // _opt.FromModifiedDateUtc is a string (ISO). Use null if blank.
                string? fromIso = string.IsNullOrWhiteSpace(_opt.FromModifiedDateUtc)
                    ? null
                    : _opt.FromModifiedDateUtc;

                xml = _request.BuildItemInventoryStart(
                    pageSize: pageSize,
                    activeOnly: _opt.ActiveOnly,
                    fromModifiedIso8601Utc: fromIso
                );

                iter.LastRequestType = "ItemInventoryQueryRq";
                _session.SetIterator(runId, iter);

                _audit.LogMessageAsync(
                    runId, "sendRequestXML", "req",
                    companyFile: strCompanyFileName,
                    message: $"type=ItemInventoryQueryRq(START); iterator=<none>; maxReturned={pageSize}; activeOnly={_opt.ActiveOnly}; fromMod={fromIso ?? "<null>"}; qbXmlVer={qbXMLMajorVers}.{qbXMLMinorVers}",
                    payloadXml: xml
                ).GetAwaiter().GetResult();

                return xml;
            }

            // ---- PHASE 3: Inventory CONTINUE ----
            {
                var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;

                xml = _request.BuildItemInventoryContinue(iter.IteratorId!, pageSize);

                iter.LastRequestType = "ItemInventoryQueryRq";
                _session.SetIterator(runId, iter);

                _audit.LogMessageAsync(
                    runId, "sendRequestXML", "req",
                    companyFile: strCompanyFileName,
                    message: $"type=ItemInventoryQueryRq(CONTINUE); iterator={iter.IteratorId}; maxReturned={pageSize}; qbXmlVer={qbXMLMajorVers}.{qbXMLMinorVers}",
                    payloadXml: xml
                ).GetAwaiter().GetResult();

                return xml;
            }
        }




        public int receiveResponseXML(string ticket, string response, string hresult, string message)
        {
            // If we can't map the ticket, end the loop.
            if (!_session.TryGetRunId(ticket, out var runId))
                return 0;

            var state = _session.GetIterator(runId);

            // ---- COMPANY BRANCH ----
            // Detect CompanyQueryRs either by our own state or by the payload
            bool isCompanyRs =
                state.LastRequestType == "CompanyQueryRq" ||
                (response?.IndexOf("<CompanyQueryRs", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isCompanyRs)
            {
                _audit.LogMessageAsync(
                    runId,
                    "receiveResponseXML",
                    "resp",
                    message: $"companyRs: hresult={hresult ?? "<null>"}; msg={message ?? "<null>"}"
                ).GetAwaiter().GetResult();

                // ✅ Mark company phase complete so next sendRequestXML issues START (unfiltered)
                state.LastRequestType = "CompanyDone";
                state.IteratorId = null;      // force a clean START on next call
                state.Remaining = 0;
                _session.SetIterator(runId, state);

                _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: "returning=1 after CompanyQueryRq"
                ).GetAwaiter().GetResult();

                return 1; // continue
            }

            // ---- INVENTORY BRANCH ----
            // Guard: empty/whitespace response → stop gracefully
            if (string.IsNullOrWhiteSpace(response))
            {
                _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: "empty response; returning=100"
                ).GetAwaiter().GetResult();
                return 100;
            }

            // Parse the qbXML into DTOs + iterator info (with safety)
            dynamic? parsed = null;
            try
            {
                parsed = _response
                    .HandleReceiveAsync(runId, response, hresult, message)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: $"parser exception: {ex.GetType().Name}: {ex.Message}"
                ).GetAwaiter().GetResult();
                return 100; // fail-safe: end this run so QBWC doesn't spin forever
            }

            // Insert batch if we actually parsed items
            var itemCount = (int?)parsed?.InventoryItems?.Count ?? 0;
            if (itemCount > 0)
            {
                try
                {
                    _import.BulkInsertInventoryAsync(runId, parsed!.InventoryItems).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _audit.LogMessageAsync(
                        runId, "receiveResponseXML", "resp",
                        message: $"BulkInsertInventory failed: {ex.GetType().Name}: {ex.Message}"
                    ).GetAwaiter().GetResult();
                    // continue; iterator/state will still advance
                }
            }

            // Advance iterator state
            state.IteratorId = parsed?.IteratorId;
            state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
            state.LastRequestType = "ItemInventoryQueryRq";
            _session.SetIterator(runId, state);

            // Extra audit crumbs for triage
            _audit.LogMessageAsync(
                runId, "receiveResponseXML", "resp",
                message: $"parsed.iteratorId={parsed?.IteratorId ?? "<null>"}; parsed.remaining={(int?)parsed?.IteratorRemaining ?? -1}"
            ).GetAwaiter().GetResult();

            _audit.LogMessageAsync(
                runId,
                "receiveResponseXML",
                "resp",
                message: $"items={itemCount}; remaining={state.Remaining}; hresult={hresult ?? "<null>"}; msg={message ?? "<null>"}"
            ).GetAwaiter().GetResult();

            var ret = state.Remaining > 0 ? 1 : 100; // 100 = done
            _audit.LogMessageAsync(
                runId,
                "receiveResponseXML",
                "resp",
                message: $"returning={ret} after ItemInventoryQueryRq"
            ).GetAwaiter().GetResult();

            return ret;
        }



    }
}
