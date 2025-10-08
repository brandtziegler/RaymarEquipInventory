using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Serilog;
using Hangfire;
using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public sealed class QbwcSoapService : IQBWebConnectorSvc
    {
        private readonly IAuditLogger _audit;
        private readonly IQBWCSessionStore _session;
        private readonly IQBWCRequestBuilder _request;
        private readonly IQBWCResponseHandler _response;
        private readonly IInventoryImportService _importInv;
        private readonly ICustomerImportService _customerImport;
        private readonly QbwcRequestOptions _opt;
        private readonly IBackgroundJobClient _jobs;
        private readonly IQBItemCatalogImportService _catalog;
        private readonly IQBItemOtherImportService _other;
        // ===== Feature flags =====
        private const bool ENABLE_CUSTOMER_SYNC = true;
        private const bool CUSTOMERS_ONLY = true;
        // NEW: master switch for NonInventory / OtherCharge / SalesTax / SalesTaxGroup
        private const bool ENABLE_OTHER_ITEMS = true;

        public QbwcSoapService(
            IAuditLogger audit,
            IQBWCSessionStore session,
            IQBWCRequestBuilder request,
            IQBWCResponseHandler response,
            IInventoryImportService importInv,
            IOptions<QbwcRequestOptions> opt,
            IBackgroundJobClient jobs,
            ICustomerImportService customerImport,
            IQBItemCatalogImportService catalog,
            IQBItemOtherImportService other)
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
            _other = other;
        }

        public string[] authenticate(string strUserName, string strPassword)
        {
            const string expectedUser = "raymar-qbwc";
            const string expectedPass = "Thr!ve2025AD";

            if (!string.Equals(strUserName, expectedUser, StringComparison.Ordinal) ||
                !string.Equals(strPassword, expectedPass, StringComparison.Ordinal))
            {
                return new[] { "", "nvu" };
            }

            var runId = _session.StartSessionAsync(strUserName ?? "unknown", companyFile: null).GetAwaiter().GetResult();
            var ticket = Guid.NewGuid().ToString("n");
            _session.MapTicketAsync(ticket, runId).GetAwaiter().GetResult();

            _audit.LogMessageAsync(runId, "authenticate", "resp", message: "ok", ct: CancellationToken.None)
                  .GetAwaiter().GetResult();

            return new[] { ticket, "" };
        }

        public string clientVersion(string strVersion) => "";
        public string serverVersion() => "TaskFuel QBWC Bridge v0.1";

        public string getLastError(string ticket)
        {
            if (_session.TryGetRunId(ticket, out var runId))
                _audit.LogMessageAsync(runId, "getLastError", "resp", message: "No error").GetAwaiter().GetResult();
            return "No error";
        }

        public string closeConnection(string ticket)
        {
            if (_session.TryGetRunId(ticket, out var runId))
                _session.EndSessionAsync(runId, null).GetAwaiter().GetResult();
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
            if (!_session.TryGetRunId(ticket, out var runId))
            {
                runId = _session.StartSessionAsync("unknown", strCompanyFileName).GetAwaiter().GetResult();
                _session.MapTicketAsync(ticket, runId).GetAwaiter().GetResult();
            }

            var iter = _session.GetIterator(runId);
            string xml;

            if (_opt.CustomersOnly && string.IsNullOrEmpty(iter.LastRequestType))
            {
                iter.LastRequestType = "CustomerStartPending";
                iter.IteratorId = null;
                iter.Remaining = 0;
                _session.SetIterator(runId, iter);
            }

            // ======================= SERVICE REQUESTS =======================
            if (!_opt.CustomersOnly)
            {
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

            // ======================= NEW: OTHER-ITEM REQUESTS =======================
            if (!_opt.CustomersOnly && ENABLE_OTHER_ITEMS)
            {
                // ----- NonInventory START / CONTINUE -----
                if (string.Equals(iter.LastRequestType, "NonInventoryStartPending", StringComparison.Ordinal))
                {
                    var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    var fromIso = string.IsNullOrWhiteSpace(_opt.FromModifiedDateUtc) ? null : _opt.FromModifiedDateUtc;

                    xml = _request.BuildItemNonInventoryStart(pageSize, _opt.ActiveOnly, fromIso);
                    iter.LastRequestType = "ItemNonInventoryQueryRq";
                    iter.IteratorId = null;
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemNonInventoryQueryRq(START); maxReturned={pageSize}; activeOnly={_opt.ActiveOnly}; fromMod={fromIso ?? "<null>"}")
                        .GetAwaiter().GetResult();

                    return xml;
                }
                if (string.Equals(iter.LastRequestType, "ItemNonInventoryQueryRq", StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(iter.IteratorId))
                {
                    var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    xml = _request.BuildItemNonInventoryContinue(iter.IteratorId!, pageSize);
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemNonInventoryQueryRq(CONTINUE); iterator={iter.IteratorId}; maxReturned={pageSize}")
                        .GetAwaiter().GetResult();

                    return xml;
                }

                // ----- OtherCharge START / CONTINUE -----
                if (string.Equals(iter.LastRequestType, "OtherChargeStartPending", StringComparison.Ordinal))
                {
                    var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    var fromIso = string.IsNullOrWhiteSpace(_opt.FromModifiedDateUtc) ? null : _opt.FromModifiedDateUtc;

                    xml = _request.BuildItemOtherChargeStart(pageSize, _opt.ActiveOnly, fromIso);
                    iter.LastRequestType = "ItemOtherChargeQueryRq";
                    iter.IteratorId = null;
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemOtherChargeQueryRq(START); maxReturned={pageSize}; activeOnly={_opt.ActiveOnly}; fromMod={fromIso ?? "<null>"}")
                        .GetAwaiter().GetResult();

                    return xml;
                }
                if (string.Equals(iter.LastRequestType, "ItemOtherChargeQueryRq", StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(iter.IteratorId))
                {
                    var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    xml = _request.BuildItemOtherChargeContinue(iter.IteratorId!, pageSize);
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemOtherChargeQueryRq(CONTINUE); iterator={iter.IteratorId}; maxReturned={pageSize}")
                        .GetAwaiter().GetResult();

                    return xml;
                }

                // ----- SalesTaxItem START / CONTINUE -----
                if (string.Equals(iter.LastRequestType, "SalesTaxStartPending", StringComparison.Ordinal))
                {
                    var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    var fromIso = string.IsNullOrWhiteSpace(_opt.FromModifiedDateUtc) ? null : _opt.FromModifiedDateUtc;

                    xml = _request.BuildItemSalesTaxStart(pageSize, _opt.ActiveOnly, fromIso);
                    iter.LastRequestType = "ItemSalesTaxQueryRq";
                    iter.IteratorId = null;
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemSalesTaxQueryRq(START); maxReturned={pageSize}; activeOnly={_opt.ActiveOnly}; fromMod={fromIso ?? "<null>"}")
                        .GetAwaiter().GetResult();

                    return xml;
                }
                if (string.Equals(iter.LastRequestType, "ItemSalesTaxQueryRq", StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(iter.IteratorId))
                {
                    var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    xml = _request.BuildItemSalesTaxContinue(iter.IteratorId!, pageSize);
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemSalesTaxQueryRq(CONTINUE); iterator={iter.IteratorId}; maxReturned={pageSize}")
                        .GetAwaiter().GetResult();

                    return xml;
                }

                // ----- SalesTaxGroup START / CONTINUE -----
                if (string.Equals(iter.LastRequestType, "SalesTaxGroupStartPending", StringComparison.Ordinal))
                {
                    var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    var fromIso = string.IsNullOrWhiteSpace(_opt.FromModifiedDateUtc) ? null : _opt.FromModifiedDateUtc;

                    xml = _request.BuildItemSalesTaxGroupStart(pageSize, _opt.ActiveOnly, fromIso);
                    iter.LastRequestType = "ItemSalesTaxGroupQueryRq";
                    iter.IteratorId = null;
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemSalesTaxGroupQueryRq(START); maxReturned={pageSize}; activeOnly={_opt.ActiveOnly}; fromMod={fromIso ?? "<null>"}")
                        .GetAwaiter().GetResult();

                    return xml;
                }
                if (string.Equals(iter.LastRequestType, "ItemSalesTaxGroupQueryRq", StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(iter.IteratorId))
                {
                    var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    xml = _request.BuildItemSalesTaxGroupContinue(iter.IteratorId!, pageSize);
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemSalesTaxGroupQueryRq(CONTINUE); iterator={iter.IteratorId}; maxReturned={pageSize}")
                        .GetAwaiter().GetResult();

                    return xml;
                }
            }

            // ======================= CUSTOMERS =======================
            if (_opt.CustomersOnly || ENABLE_CUSTOMER_SYNC)
            {
                if (string.Equals(iter.LastRequestType, "CustomerStartPending", StringComparison.Ordinal))
                {
                    var pageSizeCust = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    string? fromIsoCust = string.IsNullOrWhiteSpace(_opt.FromModifiedDateUtc) ? null : _opt.FromModifiedDateUtc;

                    xml = _request.BuildCustomerStart(pageSizeCust, _opt.ActiveOnly, fromIsoCust);

                    iter.LastRequestType = "CustomerQueryRq";
                    iter.IteratorId = null;
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(
                        runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=CustomerQueryRq(START); maxReturned={pageSizeCust}; activeOnly={_opt.ActiveOnly}; fromMod={fromIsoCust ?? "<null>"}; qbXmlVer={qbXMLMajorVers}.{qbXMLMinorVers}",
                        payloadXml: xml
                    ).GetAwaiter().GetResult();

                    return xml;
                }

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

            // ======================= COMPANY + INVENTORY (unchanged) =======================
            if (!_opt.CustomersOnly)
            {
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

                if (string.Equals(iter.LastRequestType, "CompanyDone", StringComparison.Ordinal))
                {
                    const int firstPage = 1000;

                    xml = _request.BuildItemInventoryStart(firstPage, false, null);

                    iter.LastRequestType = "ItemInventoryQueryRq";
                    iter.IteratorId = null;
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(
                        runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemInventoryQueryRq(START,unfiltered); maxReturned={firstPage}; qbXmlVer={qbXMLMajorVers}.{qbXMLMinorVers}",
                        payloadXml: xml
                    ).GetAwaiter().GetResult();

                    return xml;
                }

                if (string.IsNullOrEmpty(iter.IteratorId))
                {
                    var pageSize = _opt.PageSize > 0 ? _opt.PageSize : 500;
                    string? fromIso = string.IsNullOrWhiteSpace(_opt.FromModifiedDateUtc) ? null : _opt.FromModifiedDateUtc;

                    xml = _request.BuildItemInventoryStart(pageSize, _opt.ActiveOnly, fromIso);

                    iter.LastRequestType = "ItemInventoryQueryRq";
                    _session.SetIterator(runId, iter);

                    _audit.LogMessageAsync(
                        runId, "sendRequestXML", "req",
                        companyFile: strCompanyFileName,
                        message: $"type=ItemInventoryQueryRq(START); maxReturned={pageSize}; activeOnly={_opt.ActiveOnly}; fromMod={fromIso ?? "<null>"}; qbXmlVer={qbXMLMajorVers}.{qbXMLMinorVers}",
                        payloadXml: xml
                    ).GetAwaiter().GetResult();

                    return xml;
                }

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

            _audit.LogMessageAsync(runId, "sendRequestXML", "req", message: "customers-only: no further requests").GetAwaiter().GetResult();
            return "";
        }

        public int receiveResponseXML(string ticket, string response, string hresult, string message)
        {
            if (!_session.TryGetRunId(ticket, out var runId))
                return 0;

            var state = _session.GetIterator(runId);

            bool isCompanyRs =
                state.LastRequestType == "CompanyQueryRq" ||
                (response?.IndexOf("<CompanyQueryRs", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isCompanyRs)
            {
                _audit.LogMessageAsync(runId, "receiveResponseXML", "resp",
                    message: $"companyRs: hresult={hresult ?? "<null>"}; msg={message ?? "<null>"}")
                    .GetAwaiter().GetResult();

                state.LastRequestType = "CompanyDone";
                state.IteratorId = null;
                state.Remaining = 0;
                _session.SetIterator(runId, state);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                _audit.LogMessageAsync(runId, "receiveResponseXML", "resp", message: "empty response; returning=100")
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
                _audit.LogMessageAsync(runId, "receiveResponseXML", "resp",
                    message: $"parser exception: {ex.GetType().Name}: {ex.Message}")
                    .GetAwaiter().GetResult();
                return 100;
            }

            // ======================= INVENTORY =======================
            if (!_opt.CustomersOnly)
            {
                var itemCount = (int?)parsed?.InventoryItems?.Count ?? 0;
                if (itemCount > 0)
                {
                    try
                    {
                        _importInv.BulkInsertInventoryAsync(runId, parsed!.InventoryItems).GetAwaiter().GetResult();

                    }
                    catch (Exception ex)
                    {
                        _audit.LogMessageAsync(runId, "receiveResponseXML", "resp",
                            message: $"BulkInsertInventory failed: {ex.GetType().Name}: {ex.Message}")
                            .GetAwaiter().GetResult();
                    }

                    state.IteratorId = parsed?.IteratorId;
                    state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                    state.LastRequestType = "ItemInventoryQueryRq";
                    _session.SetIterator(runId, state);

                    if (state.Remaining <= 0)
                    {
                        state.LastRequestType = "ServiceStartPending";
                        state.IteratorId = null;
                        state.Remaining = 0;
                        _session.SetIterator(runId, state);
                        return 1;
                    }
                    return 1;
                }

                // ======================= SERVICE =======================
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
                        _audit.LogMessageAsync(runId, "receiveResponseXML", "resp",
                            message: $"BulkInsertServiceItems failed: {ex.GetType().Name}: {ex.Message}")
                            .GetAwaiter().GetResult();
                    }

                    state.IteratorId = parsed?.IteratorId;
                    state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                    state.LastRequestType = "ItemServiceQueryRq";
                    _session.SetIterator(runId, state);

                    if (state.Remaining <= 0)
                    {
                        if (ENABLE_OTHER_ITEMS)
                        {
                            state.LastRequestType = "NonInventoryStartPending";
                            state.IteratorId = null;
                            state.Remaining = 0;
                            _session.SetIterator(runId, state);
                            return 1;
                        }

                        if (ENABLE_CUSTOMER_SYNC)
                        {
                            state.LastRequestType = "CustomerStartPending";
                            state.IteratorId = null;
                            state.Remaining = 0;
                            _session.SetIterator(runId, state);
                            return 1;
                        }
                    }

                    return state.Remaining > 0 ? 1 : 100;
                }

                // ======================= NEW: NON-INVENTORY =======================
                bool isNonInvRs =
                    state.LastRequestType == "ItemNonInventoryQueryRq" ||
                    (response?.IndexOf("<ItemNonInventoryQueryRs", StringComparison.OrdinalIgnoreCase) >= 0);

                if (ENABLE_OTHER_ITEMS && isNonInvRs)
                {
                    try
                    {
                        var list = (IEnumerable<CatalogItemDto>)parsed?.ServiceItems ?? Enumerable.Empty<CatalogItemDto>();
                        var batch = list.Where(x => string.Equals(x.Type, "NonInventory", StringComparison.OrdinalIgnoreCase)).ToList();

                        if (batch.Count > 0)
                        {
                            // First “Other Item” import → truncate QBItemOther_Staging
                            bool firstPage = string.Equals(state.LastRequestType, "ItemNonInventoryQueryRq", StringComparison.Ordinal)
                                             && string.IsNullOrEmpty(state.IteratorId);

                            _other.BulkInsertOtherItemsAsync(runId, batch, firstPage).GetAwaiter().GetResult();
                        }
                    }
                    catch (Exception ex)
                    {
                        _audit.LogMessageAsync(runId, "QBItemOther_Staging", "resp",
                            message: $"BulkInsertOtherItems (NonInventory) failed: {ex.GetType().Name}: {ex.Message}")
                            .GetAwaiter().GetResult();
                    }

                    state.IteratorId = parsed?.IteratorId;
                    state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                    state.LastRequestType = "ItemNonInventoryQueryRq";
                    _session.SetIterator(runId, state);

                    if (state.Remaining <= 0)
                    {
                        state.LastRequestType = "OtherChargeStartPending";
                        state.IteratorId = null;
                        state.Remaining = 0;
                        _session.SetIterator(runId, state);
                        return 1;
                    }
                    return 1;
                }

                // ======================= NEW: OTHER-CHARGE =======================
                bool isOtherChargeRs =
                    state.LastRequestType == "ItemOtherChargeQueryRq" ||
                    (response?.IndexOf("<ItemOtherChargeQueryRs", StringComparison.OrdinalIgnoreCase) >= 0);

                if (ENABLE_OTHER_ITEMS && isOtherChargeRs)
                {
                    try
                    {
                        var list = (IEnumerable<CatalogItemDto>)parsed?.ServiceItems ?? Enumerable.Empty<CatalogItemDto>();
                        var batch = list.Where(x => string.Equals(x.Type, "OtherCharge", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (batch.Count > 0)
                            _other.BulkInsertOtherItemsAsync(runId, batch, false).GetAwaiter().GetResult();   // append only
                    }
                    catch (Exception ex)
                    {
                        _audit.LogMessageAsync(runId, "QBItemOther_Staging", "resp",
                            message: $"BulkInsertOtherItems (OtherCharge) failed: {ex.GetType().Name}: {ex.Message}")
                            .GetAwaiter().GetResult();
                    }

                    state.IteratorId = parsed?.IteratorId;
                    state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                    state.LastRequestType = "ItemOtherChargeQueryRq";
                    _session.SetIterator(runId, state);

                    if (state.Remaining <= 0)
                    {
                        state.LastRequestType = "SalesTaxStartPending";
                        state.IteratorId = null;
                        state.Remaining = 0;
                        _session.SetIterator(runId, state);
                        return 1;
                    }
                    return 1;
                }

                // ======================= NEW: SALES-TAX ITEM =======================
                bool isSalesTaxRs =
                    state.LastRequestType == "ItemSalesTaxQueryRq" ||
                    (response?.IndexOf("<ItemSalesTaxQueryRs", StringComparison.OrdinalIgnoreCase) >= 0);

                if (ENABLE_OTHER_ITEMS && isSalesTaxRs)
                {
                    try
                    {
                        var list = (IEnumerable<CatalogItemDto>)parsed?.ServiceItems ?? Enumerable.Empty<CatalogItemDto>();
                        var batch = list.Where(x => string.Equals(x.Type, "SalesTaxItem", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (batch.Count > 0)
                            _other.BulkInsertOtherItemsAsync(runId, batch, false).GetAwaiter().GetResult();   // append only
                    }
                    catch (Exception ex)
                    {
                        _audit.LogMessageAsync(runId, "QBItemOther_Staging", "resp",
                            message: $"BulkInsertOtherItems (SalesTaxItem) failed: {ex.GetType().Name}: {ex.Message}")
                            .GetAwaiter().GetResult();
                    }

                    state.IteratorId = parsed?.IteratorId;
                    state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                    state.LastRequestType = "ItemSalesTaxQueryRq";
                    _session.SetIterator(runId, state);

                    if (state.Remaining <= 0)
                    {
                        state.LastRequestType = "SalesTaxGroupStartPending";
                        state.IteratorId = null;
                        state.Remaining = 0;
                        _session.SetIterator(runId, state);
                        return 1;
                    }
                    return 1;
                }

                // ======================= NEW: SALES-TAX GROUP =======================
                bool isSalesTaxGroupRs =
                    state.LastRequestType == "ItemSalesTaxGroupQueryRq" ||
                    (response?.IndexOf("<ItemSalesTaxGroupQueryRs", StringComparison.OrdinalIgnoreCase) >= 0);

                if (ENABLE_OTHER_ITEMS && isSalesTaxGroupRs)
                {
                    try
                    {
                        var list = (IEnumerable<CatalogItemDto>)parsed?.ServiceItems ?? Enumerable.Empty<CatalogItemDto>();
                        var batch = list.Where(x => string.Equals(x.Type, "SalesTaxGroup", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (batch.Count > 0)
                            _other.BulkInsertOtherItemsAsync(runId, batch, false).GetAwaiter().GetResult();   // append only
                    }
                    catch (Exception ex)
                    {
                        _audit.LogMessageAsync(runId, "QBItemOther_Staging", "resp",
                            message: $"BulkInsertOtherItems (SalesTaxGroup) failed: {ex.GetType().Name}: {ex.Message}")
                            .GetAwaiter().GetResult();
                    }

                    state.IteratorId = parsed?.IteratorId;
                    state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                    state.LastRequestType = "ItemSalesTaxGroupQueryRq";
                    _session.SetIterator(runId, state);

                    if (state.Remaining <= 0)
                    {
                        if (ENABLE_CUSTOMER_SYNC)
                        {
                            state.LastRequestType = "CustomerStartPending";
                            state.IteratorId = null;
                            state.Remaining = 0;
                            _session.SetIterator(runId, state);
                            return 1;
                        }
                    }
                    return state.Remaining > 0 ? 1 : 100;
                }

            }

            // ======================= CUSTOMERS =======================
            var custCount = (int?)parsed?.Customers?.Count ?? 0;

            bool isCustomerRs =
                state.LastRequestType == "CustomerQueryRq" ||
                (response?.IndexOf("<CustomerQueryRs", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isCustomerRs && custCount == 0)
            {
                state.IteratorId = parsed?.IteratorId;
                state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                state.LastRequestType = "CustomerQueryRq";
                _session.SetIterator(runId, state);
                return state.Remaining > 0 ? 1 : 100;
            }

            if (custCount > 0)
            {
                try
                {
                    bool firstPage = string.Equals(state.LastRequestType, "CustomerQueryRq", StringComparison.Ordinal)
                                     && string.IsNullOrEmpty(state.IteratorId);

                    bool lastPage = parsed?.IteratorRemaining == 0;

                    _audit.LogMessageAsync(runId, "CustomerBackup", "resp",
                        message: $"about to bulk: custCount={(int?)parsed?.Customers?.Count ?? 0}, firstPage={string.IsNullOrEmpty(state.IteratorId)}")
                        .GetAwaiter().GetResult();

                    _customerImport.BulkInsertCustomersAsync(runId, parsed.Customers, firstPage, lastPage).GetAwaiter().GetResult();

                }
                catch (Exception ex)
                {
                    _audit.LogMessageAsync(runId, "receiveResponseXML", "resp",
                        message: $"BulkInsertCustomers failed: {ex.GetType().Name}: {ex.Message}")
                        .GetAwaiter().GetResult();
                }

                state.IteratorId = parsed?.IteratorId;
                state.Remaining = (int?)parsed?.IteratorRemaining ?? 0;
                state.LastRequestType = "CustomerQueryRq";
                _session.SetIterator(runId, state);

                var ret = state.Remaining > 0 ? 1 : 100;
                return ret;
            }

            _audit.LogMessageAsync(runId, "receiveResponseXML", "resp",
                message: "no recognized payload (not Company, Inventory, Service, OtherItems, or Customers); returning=100")
                .GetAwaiter().GetResult();

            return 100;
        }

    }
}
