using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Data;                    // for SqlDbType
using System.Threading;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;

using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Concurrent;
using RaymarEquipmentInventory.DTOs;
using System.Globalization;
using System.Xml.Linq;


namespace RaymarEquipmentInventory.Services
{
    /// Logs the payload, extracts iterator info, parses items → DTOs.
    public sealed class QbwcResponseHandler : IQBWCResponseHandler
    {
        private readonly IAuditLogger _audit;

        public QbwcResponseHandler(IAuditLogger audit)
        {
            _audit = audit;
        }

        public async Task<ReceiveParseResult> HandleReceiveAsync(
            Guid runId,
            string responseXml,
            string? hresult,
            string? message,
            CancellationToken ct = default)
        {
            // 1) Always audit the raw qbXML
            await _audit.LogMessageAsync(
                runId,
                method: "receiveResponseXML",
                direction: "req",
                statusCode: null,
                hresult: hresult,
                message: message,
                companyFile: null,
                payloadXml: responseXml,
                ct: ct);

            // 2) Parse
            var result = new ReceiveParseResult();

            var doc = XDocument.Parse(responseXml);

            // ItemInventoryQueryRs node carries iterator attrs and status
            var rs = doc.Descendants("ItemInventoryQueryRs").FirstOrDefault();
            if (rs != null)
            {
                var iterRemainAttr = (string?)rs.Attribute("iteratorRemainingCount");
                var iterIdAttr = (string?)rs.Attribute("iteratorID");
                var statusCodeAttr = (string?)rs.Attribute("statusCode");
                var statusMsgAttr = (string?)rs.Attribute("statusMessage");

                if (int.TryParse(iterRemainAttr, out var remain)) result.IteratorRemaining = remain;
                result.IteratorId = iterIdAttr;
                if (int.TryParse(statusCodeAttr, out var sc)) result.StatusCode = sc;
                result.StatusMessage = statusMsgAttr;

                var items =
                    from it in rs.Elements("ItemInventoryRet")
                    select new InventoryItemDto
                    {
                        ListID = (string?)it.Element("ListID"),
                        FullName = (string?)it.Element("FullName"),
                        EditSequence = (string?)it.Element("EditSequence"),
                        QuantityOnHand = TryDec((string?)it.Element("QuantityOnHand")),
                        SalesPrice = TryDec((string?)it.Element("SalesPrice")),
                        PurchaseCost = TryDec((string?)it.Element("PurchaseCost")),
                        TimeModified = TryDate((string?)it.Element("TimeModified"))
                    };

                result.InventoryItems.AddRange(items);
            }

            return result;
        }

        private static decimal? TryDec(string? s)
            => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

        private static DateTime? TryDate(string? s)
            => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
    }
}
