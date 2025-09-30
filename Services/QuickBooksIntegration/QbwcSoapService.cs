using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Serilog;
using Hangfire;

namespace RaymarEquipmentInventory.Services
{
    public sealed class QbwcSoapService : IQBWebConnectorSvc
    {
        private readonly IAuditLogger _audit;
        private readonly IQBWCSessionStore _session;
        private readonly IQBWCRequestBuilder _request;
        private readonly IQBWCResponseHandler _response;
        private readonly IInventoryImportService _importInv;
        private readonly ICustomerImportService _customerImport;    // <-- added in your code
        private readonly QbwcRequestOptions _opt;
        private readonly IBackgroundJobClient _jobs;
        private readonly IQBItemCatalogImportService _catalog;

        // ======= Feature flag: set TRUE to enable Customer sync after Inventory =======
        private const bool ENABLE_CUSTOMER_SYNC = true; // <-- flip to true when ready
        private const bool CUSTOMERS_ONLY = true;

        public QbwcSoapService(
            IAuditLogger audit,
            IQBWCSessionStore session,
            IQBWCRequestBuilder request,
            IQBWCResponseHandler response,
            IInventoryImportService importInv,
            IOptions<QbwcRequestOptions> opt,
            IBackgroundJobClient jobs,
            ICustomerImportService customerImport,
            IQBItemCatalogImportService catalog)  // <-- already present in your code
        {
            _audit = audit;
            _session = session;
            _request = request;
            _response = response;
            _importInv = importInv;
            _opt = opt.Value ?? new QbwcRequestOptions();
            _jobs = jobs;
            _customerImport = customerImport;
            _catalog = catalog;
        }

        // ---- SOAP ops ---- soap service.

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

            // --- Customers-only bootstrap: on first tick, force Customer START ---
            if (_opt.CustomersOnly && string.IsNullOrEmpty(iter.LastRequestType))
            {
                iter.LastRequestType = "CustomerStartPending";
                iter.IteratorId = null;
                iter.Remaining = 0;
                _session.SetIterator(runId, iter);
            }

            // ======================= SERVICE REQUESTS (guarded off in customers-only) =======================
            if (!_opt.CustomersOnly)
            {
                // START Service items if scheduled
                if (string.Equals(iter.LastRequestType, "ServiceStartPending", StringComparison.Ordinal))
                {
                    var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    string? fromIso = string.IsNullOrWhiteSpace(_opt.FromModifiedDateUtc) ? null : _opt.FromModifiedDateUtc;

                    xml = _request.BuildItemServiceStart(pageSize, _opt.ActiveOnly, fromIso);
                    iter.LastRequestType = "ItemServiceQueryRq";
                    iter.IteratorId = null;
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemServiceQueryRq(START); maxReturned={pageSize}; activeOnly={_opt.ActiveOnly}; fromMod={fromIso ?? "<null>"}")
                        .GetAwaiter().GetResult();

                    return xml;
                }

                // CONTINUE Service items if mid-iteration
                if (string.Equals(iter.LastRequestType, "ItemServiceQueryRq", StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(iter.IteratorId))
                {
                    var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    xml = _request.BuildItemServiceContinue(iter.IteratorId!, pageSize);

                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemServiceQueryRq(CONTINUE); iterator={iter.IteratorId}; maxReturned={pageSize}")
                        .GetAwaiter().GetResult();

                    return xml;
                }
            }

            // ======================= CUSTOMERS REQUESTS =======================
            if (_opt.CustomersOnly || ENABLE_CUSTOMER_SYNC)
            {
                // START customers if we scheduled it
                if (string.Equals(iter.LastRequestType, "CustomerStartPending", StringComparison.Ordinal))
                {
                    var pageSizeCust = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    string? fromIsoCust = string.IsNullOrWhiteSpace(_opt.FromModifiedDateUtc) ? null : _opt.FromModifiedDateUtc;

                    xml = _request.BuildCustomerStart(
                        pageSize: pageSizeCust,
                        activeOnly: _opt.ActiveOnly,
                        fromModifiedIso8601Utc: fromIsoCust);

                    iter.LastRequestType = "CustomerQueryRq";
                    iter.IteratorId = null; // explicit START
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(
                        runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=CustomerQueryRq(START); iterator=<none>; maxReturned={pageSizeCust}; activeOnly={_opt.ActiveOnly}; fromMod={fromIsoCust ?? "<null>"}; qbXmlVer={qbXMLMajorVers}.{qbXMLMinorVers}",
                        payloadXml: xml
                    ).GetAwaiter().GetResult();

                    return xml;
                }

                // CONTINUE customers if we're mid-iteration
                if (string.Equals(iter.LastRequestType, "CustomerQueryRq", StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(iter.IteratorId))
                {
                    var pageSizeCust = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    xml = _request.BuildCustomerContinue(iter.IteratorId!, pageSizeCust);

                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(
                        runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=CustomerQueryRq(CONTINUE); iterator={iter.IteratorId}; maxReturned={pageSizeCust}; qbXmlVer={qbXMLMajorVers}.{qbXMLMinorVers}",
                        payloadXml: xml
                    ).GetAwaiter().GetResult();

                    return xml;
                }
            }

            // ======================= COMPANY + INVENTORY REQUESTS (guarded off in customers-only) =======================
            if (!_opt.CustomersOnly)
            {
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

            // If we reach here in customers-only mode and no request matched, tell QBWC we're done
            _audit.LogMessageAsync(runId, "sendRequestXML", "req", message: "customers-only: no further requests").GetAwaiter().GetResult();
            return "";
        }




        public int receiveResponseXML(string ticket, string response, string hresult, string message)
        {
            // If we can't map the ticket, end the loop.
            if (!_session.TryGetRunId(ticket, out var runId))
                return 0;

            var state = _session.GetIterator(runId);

            // ---- COMPANY BRANCH ----
            bool isCompanyRs =
                state.LastRequestType == "CompanyQueryRq" ||
                (response?.IndexOf("<CompanyQueryRs", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isCompanyRs)
            {
                _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: $"companyRs: hresult={hresult ?? "<null>"}; msg={message ?? "<null>"}"
                ).GetAwaiter().GetResult();

                // Next call should issue Inventory START (unfiltered)
                state.LastRequestType = "CompanyDone";
                state.IteratorId = null;
                state.Remaining = 0;
                _session.SetIterator(runId, state);

                _audit.LogMessageAsync(runId, "receiveResponseXML", "resp",
                    message: "returning=1 after CompanyQueryRq").GetAwaiter().GetResult();

                return 1;
            }

            // ---- GUARD: empty/whitespace response → stop
            if (string.IsNullOrWhiteSpace(response))
            {
                _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: "empty response; returning=100"
                ).GetAwaiter().GetResult();
                return 100;
            }

            // ---- Parse qbXML into DTOs + iterator info
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
                return 100; // fail-safe
            }

            // ======================= INVENTORY + SERVICE RESPONSES (guarded off in customers-only) =======================
            if (!_opt.CustomersOnly)
            {
                // ======================= INVENTORY BRANCH =======================
                var itemCount = (int?)parsed?.InventoryItems?.Count ?? 0;
                if (itemCount > 0)
                {
                    try
                    {
                        _importInv.BulkInsertInventoryAsync(runId, parsed!.InventoryItems).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _audit.LogMessageAsync(
                            runId, "receiveResponseXML", "resp",
                            message: $"BulkInsertInventory failed: {ex.GetType().Name}: {ex.Message}"
                        ).GetAwaiter().GetResult();
                    }

                    // Advance iterator (inventory)
                    state.IteratorId = parsed?.IteratorId;
                    state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                    state.LastRequestType = "ItemInventoryQueryRq";
                    _session.SetIterator(runId, state);

                    _audit.LogMessageAsync(
                        runId, "receiveResponseXML", "resp",
                        message: $"items={itemCount}; remaining={state.Remaining}; hresult={hresult ?? "<null>"}; msg={message ?? "<null>"}"
                    ).GetAwaiter().GetResult();

                    var ret = state.Remaining > 0 ? 1 : 100;

                    // Enqueue promotion only on last page, then chain to Service
                    if (state.Remaining <= 0)
                    {
                        //var jobId = _jobs.Enqueue<IInventoryImportService>(
                        //    svc => svc.SyncInventoryDataAsync(runId, default)
                        //);
                        //_audit.LogMessageAsync(runId, "hangfire", "enqueue",
                        //    message: $"Inventory promote job {jobId} enqueued").GetAwaiter().GetResult();

                        state.LastRequestType = "ServiceStartPending";
                        state.IteratorId = null;
                        state.Remaining = 0;
                        _session.SetIterator(runId, state);

                        _audit.LogMessageAsync(
                            runId, "receiveResponseXML", "resp",
                            message: "Inventory complete; scheduling ItemServiceQuery START (returning=1)"
                        ).GetAwaiter().GetResult();

                        return 1; // continue so sendRequestXML issues Service START
                    }

                    _audit.LogMessageAsync(
                        runId, "receiveResponseXML", "resp",
                        message: $"returning={ret} after ItemInventoryQueryRq"
                    ).GetAwaiter().GetResult();

                    return ret;
                }

                // ======================= SERVICE ITEMS BRANCH =======================
                bool isServiceRs =
                    state.LastRequestType == "ItemServiceQueryRq" ||
                    (response?.IndexOf("<ItemServiceQueryRs", StringComparison.OrdinalIgnoreCase) >= 0);

                if (isServiceRs)
                {
                    var svcCount = (int?)parsed?.ServiceItems?.Count ?? 0;

                    try
                    {
                        if (svcCount > 0)
                            _catalog.BulkInsertServiceItemsAsync(runId, parsed!.ServiceItems).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _audit.LogMessageAsync(
                            runId, "receiveResponseXML", "resp",
                            message: $"BulkInsertServiceItems failed: {ex.GetType().Name}: {ex.Message}"
                        ).GetAwaiter().GetResult();
                    }

                    // Advance iterator (service) even when zero rows
                    state.IteratorId = parsed?.IteratorId;
                    state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                    state.LastRequestType = "ItemServiceQueryRq";
                    _session.SetIterator(runId, state);

                    var ret = state.Remaining > 0 ? 1 : 100;

                    if (state.Remaining <= 0 && ENABLE_CUSTOMER_SYNC)
                    {
                        state.LastRequestType = "CustomerStartPending";
                        state.IteratorId = null;
                        state.Remaining = 0;
                        _session.SetIterator(runId, state);

                        _audit.LogMessageAsync(
                            runId, "receiveResponseXML", "resp",
                            message: $"service.items={svcCount}; complete; scheduling CustomerQuery START (returning=1)"
                        ).GetAwaiter().GetResult();

                        return 1; // continue so sendRequestXML issues Customer START
                    }

                    _audit.LogMessageAsync(
                        runId, "receiveResponseXML", "resp",
                        message: $"service.items={svcCount}; remaining={state.Remaining}"
                    ).GetAwaiter().GetResult();

                    return ret; // continue Service CONTINUE
                }
            }

            // ======================= CUSTOMERS BRANCH =======================
            var custCount = (int?)parsed?.Customers?.Count ?? 0;

            // Even if custCount == 0, advance/finish cleanly when the response type is CustomerQueryRs
            bool isCustomerRs =
                state.LastRequestType == "CustomerQueryRq" ||
                (response?.IndexOf("<CustomerQueryRs", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isCustomerRs && custCount == 0)
            {
                state.IteratorId = parsed?.IteratorId;
                state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                state.LastRequestType = "CustomerQueryRq";
                _session.SetIterator(runId, state);

                _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: $"customers=0; remaining={state.Remaining}; iteratorId={parsed?.IteratorId ?? "<null>"}"
                ).GetAwaiter().GetResult();

                return state.Remaining > 0 ? 1 : 100;
            }

            if (custCount > 0)
            {
                try
                {
                    bool firstPage = string.Equals(state.LastRequestType, "CustomerQueryRq", StringComparison.Ordinal)
                                     && string.IsNullOrEmpty(state.IteratorId);   // ← first page

                    _audit.LogMessageAsync(runId, "CustomerBackup", "resp",
                        message: $"about to bulk: custCount={(int?)parsed?.Customers?.Count ?? 0}, firstPage={string.IsNullOrEmpty(state.IteratorId)}")
                        .GetAwaiter().GetResult();

                    _customerImport.BulkInsertCustomersAsync(runId, parsed.Customers, firstPage).GetAwaiter().GetResult();

                }
                catch (Exception ex)
                {
                    _audit.LogMessageAsync(
                        runId, "receiveResponseXML", "resp",
                        message: $"BulkInsertCustomers failed: {ex.GetType().Name}: {ex.Message}"
                    ).GetAwaiter().GetResult();
                }

                // Advance iterator (customers)
                state.IteratorId = parsed?.IteratorId;
                state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                state.LastRequestType = "CustomerQueryRq";
                _session.SetIterator(runId, state);

                _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: $"customers={custCount}; remaining={state.Remaining}; iteratorId={parsed?.IteratorId ?? "<null>"}"
                ).GetAwaiter().GetResult();

                var ret = state.Remaining > 0 ? 1 : 100;

                // Optional: enqueue promotion on last page
                if (state.Remaining <= 0)
                {
                    //var jobId = _jobs.Enqueue<ICustomerImportService>(
                    //    svc => svc.SyncCustomerDataAsync(runId, /* fullRefresh: */ false, default)
                    //);
                    //_audit.LogMessageAsync(runId, "hangfire", "enqueue",
                    //    message: $"Customer promote job {jobId} enqueued").GetAwaiter().GetResult();
                }

                _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: $"returning={ret} after CustomerQueryRq"
                ).GetAwaiter().GetResult();

                return ret;
            }

            // ---- No recognized payload
            _audit.LogMessageAsync(
                runId, "receiveResponseXML", "resp",
                message: "no recognized payload (not Company, Inventory, Service, or Customers); returning=100"
            ).GetAwaiter().GetResult();

            return 100;
        }





    }
}
