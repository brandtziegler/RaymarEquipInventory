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
            // Start a new run/session and issue a ticket
            var runId = _session.StartSessionAsync(strUserName ?? "unknown", companyFile: null).GetAwaiter().GetResult();
            var ticket = Guid.NewGuid().ToString("n");
            _session.MapTicketAsync(ticket, runId).GetAwaiter().GetResult();

            // Also log that we created a ticket
            _audit.LogMessageAsync(runId, "authenticate", "resp", message: "ok", ct: CancellationToken.None)
                  .GetAwaiter().GetResult();

            // Return [ticket, companyFilePath] — second can be blank
            return new[] { ticket, "" };
        }

        public string clientVersion(string strVersion)
        {
            // Keep it simple for now; can add stricter checks later
            return "OK";
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

        public string sendRequestXML(string ticket, string strHCPResponse, string strCompanyFileName, string qbXMLCountry, int qbXMLMajorVers, int qbXMLMinorVers)
        {
            if (!_session.TryGetRunId(ticket, out var runId))
            {
                // Safety: if authenticate didn't map (shouldn't happen), start one
                runId = _session.StartSessionAsync("unknown", strCompanyFileName).GetAwaiter().GetResult();
                _session.MapTicketAsync(ticket, runId).GetAwaiter().GetResult();
            }

            // Decide what to send
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

            // Optional: audit the outgoing qbXML we requested
            _audit.LogMessageAsync(runId, "sendRequestXML", "resp", message: "qbXML request issued",
                                   companyFile: strCompanyFileName, payloadXml: xml)
                  .GetAwaiter().GetResult();

            return xml;
        }

        public int receiveResponseXML(string ticket, string response, string hresult, string message)
        {
            if (!_session.TryGetRunId(ticket, out var runId)) return 0;

            var state = _session.GetIterator(runId);
            var parsed = _response.HandleReceiveAsync(runId, response, hresult, message).GetAwaiter().GetResult();

            // If we just handled CompanyQuery, keep the loop going to Inventory
            bool isCompany = state.LastRequestType == "CompanyQueryRq" || response.Contains("<CompanyQueryRs");
            if (isCompany)
            {
                // Company XML is already logged to QbwcMessage.PayloadXml.
                state.LastRequestType = null;            // next sendRequestXML will choose Inventory Start
                _session.SetIterator(runId, state);
                return 100;                              // tell QBWC: continue
            }

            // Inventory batch
            if (parsed.InventoryItems.Count > 0)
                _import.BulkInsertInventoryAsync(runId, parsed.InventoryItems).GetAwaiter().GetResult();

            state.IteratorId = parsed.IteratorId;
            state.Remaining = parsed.IteratorRemaining;
            state.LastRequestType = "ItemInventoryQueryRq";
            _session.SetIterator(runId, state);

            return parsed.IteratorRemaining > 0 ? 100 : 0; // 100=continue, 0=done
        }

    }
}
