using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            if (rs == null) return result;

            var iterRemainAttr = (string?)rs.Attribute("iteratorRemainingCount");
            var iterIdAttr = (string?)rs.Attribute("iteratorID");
            var statusCodeAttr = (string?)rs.Attribute("statusCode");
            var statusMsgAttr = (string?)rs.Attribute("statusMessage");

            if (int.TryParse(iterRemainAttr, out var remain)) result.IteratorRemaining = remain;
            result.IteratorId = iterIdAttr;
            if (int.TryParse(statusCodeAttr, out var sc)) result.StatusCode = sc;
            result.StatusMessage = statusMsgAttr;

            // 3) Items
            var items =
                from it in rs.Elements("ItemInventoryRet")
                let sop = it.Element("SalesOrPurchase")
                let sap = it.Element("SalesAndPurchase")
                select new InventoryItemDto
                {
                    ListID = (string?)it.Element("ListID"),
                    Name = (string?)it.Element("Name") ?? (string?)it.Element("FullName"),
                    FullName = (string?)it.Element("FullName"),
                    EditSequence = (string?)it.Element("EditSequence"),

                    QuantityOnHand = TryDec((string?)it.Element("QuantityOnHand")),

                    // price/cost can be at top level or under aggregates
                    SalesPrice = TryDec((string?)it.Element("SalesPrice")
                                           ?? (string?)sop?.Element("Price")),
                    PurchaseCost = TryDec((string?)it.Element("PurchaseCost")
                                           ?? (string?)sap?.Element("PurchaseCost")),

                    // descriptions may be nested
                    SalesDesc = (string?)it.Element("SalesDesc")
                                    ?? (string?)sop?.Element("SalesDesc")
                                    ?? (string?)sap?.Element("SalesDesc"),
                    PurchaseDesc = (string?)it.Element("PurchaseDesc")
                                    ?? (string?)sap?.Element("PurchaseDesc"),

                    ManufacturerPartNum = (string?)it.Element("ManufacturerPartNumber"),

                    TimeModified = TryDate((string?)it.Element("TimeModified"))
                };

            // materialize once, add, and (optional) quick audit
            var list = items.ToList();
            result.InventoryItems.AddRange(list);

            await _audit.LogMessageAsync(
                runId, "receiveResponseXML", "resp",
                message:
                    $"items={list.Count}; " +
                    $"withName={list.Count(i => !string.IsNullOrEmpty(i.Name))}; " +
                    $"withSalesDesc={list.Count(i => !string.IsNullOrEmpty(i.SalesDesc))}; " +
                    $"withPurchaseDesc={list.Count(i => !string.IsNullOrEmpty(i.PurchaseDesc))}; " +
                    $"withMPN={list.Count(i => !string.IsNullOrEmpty(i.ManufacturerPartNum))}",
                ct: ct);

            return result;
        }

        private static decimal? TryDec(string? s) =>
            decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null;

        // Normalize to UTC; handles offsets if present
        private static DateTime? TryDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
                ? dto.UtcDateTime
                : (DateTime?)null;
        }
    }
}

