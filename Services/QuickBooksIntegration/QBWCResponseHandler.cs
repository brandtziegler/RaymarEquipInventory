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

            // ---------- INVENTORY BRANCH (unchanged behavior) ----------
            var invRs = doc.Descendants("ItemInventoryQueryRs").FirstOrDefault();
            if (invRs != null)
            {
                PopulateIterator(invRs, result);

                var items =
                    from it in invRs.Elements("ItemInventoryRet")
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

            // ---------- SERVICE ITEMS (ItemServiceQueryRs) ----------
            var svcRs = doc.Descendants("ItemServiceQueryRs").FirstOrDefault();
            if (svcRs != null)
            {
                PopulateIterator(svcRs, result);

                var items =
                    from it in svcRs.Elements("ItemServiceRet")
                    let sop = it.Element("SalesOrPurchase")
                    let sap = it.Element("SalesAndPurchase")
                    select new CatalogItemDto
                    {
                        ListID = (string?)it.Element("ListID"),
                        Name = (string?)it.Element("Name") ?? (string?)it.Element("FullName"),
                        FullName = (string?)it.Element("FullName"),
                        EditSequence = (string?)it.Element("EditSequence"),
                        SalesPrice = TryDec((string?)it.Element("SalesPrice") ?? (string?)sop?.Element("Price")),
                        PurchaseCost = TryDec((string?)it.Element("PurchaseCost") ?? (string?)sap?.Element("PurchaseCost")),
                        SalesDesc = (string?)it.Element("SalesDesc") ?? (string?)sop?.Element("SalesDesc") ?? (string?)sap?.Element("SalesDesc"),
                        PurchaseDesc = (string?)it.Element("PurchaseDesc") ?? (string?)sap?.Element("PurchaseDesc"),
                        IsActive = TryBool((string?)it.Element("IsActive")),
                        TimeModified = TryDate((string?)it.Element("TimeModified")),
                        Type = "Service"
                    };

                var list = items.ToList();
                result.ServiceItems.AddRange(list);

                await _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: $"service.items={list.Count}; withName={list.Count(i => !string.IsNullOrEmpty(i.Name))}",
                    ct: ct);

                return result;
            }

            // =================================================================
            // NEW: NON-INVENTORY / OTHER-CHARGE / SALES-TAX / SALES-TAX-GROUP
            // =================================================================

            // ---------- ItemNonInventoryQueryRs ----------
            var nonInvRs = doc.Descendants("ItemNonInventoryQueryRs").FirstOrDefault();
            if (nonInvRs != null)
            {
                PopulateIterator(nonInvRs, result);

                var items =
                    from it in nonInvRs.Elements("ItemNonInventoryRet")
                    let sop = it.Element("SalesOrPurchase")
                    let sap = it.Element("SalesAndPurchase")
                    select new CatalogItemDto
                    {
                        ListID = (string?)it.Element("ListID"),
                        Name = (string?)it.Element("Name") ?? (string?)it.Element("FullName"),
                        FullName = (string?)it.Element("FullName"),
                        EditSequence = (string?)it.Element("EditSequence"),
                        SalesPrice = TryDec((string?)it.Element("SalesPrice") ?? (string?)sop?.Element("Price")),
                        PurchaseCost = TryDec((string?)it.Element("PurchaseCost") ?? (string?)sap?.Element("PurchaseCost")),
                        SalesDesc = (string?)it.Element("SalesDesc") ?? (string?)sop?.Element("SalesDesc") ?? (string?)sap?.Element("SalesDesc"),
                        PurchaseDesc = (string?)it.Element("PurchaseDesc") ?? (string?)sap?.Element("PurchaseDesc"),
                        IsActive = TryBool((string?)it.Element("IsActive")),
                        TimeModified = TryDate((string?)it.Element("TimeModified")),
                        Type = "NonInventory"
                    };

                var list = items.ToList();
                result.ServiceItems.AddRange(list);

                await _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: $"noninv.items={list.Count}",
                    ct: ct);

                return result;
            }

            // ---------- ItemOtherChargeQueryRs ----------
            var otherRs = doc.Descendants("ItemOtherChargeQueryRs").FirstOrDefault();
            if (otherRs != null)
            {
                PopulateIterator(otherRs, result);

                var items =
                    from it in otherRs.Elements("ItemOtherChargeRet")
                    let sop = it.Element("SalesOrPurchase")
                    select new CatalogItemDto
                    {
                        ListID = (string?)it.Element("ListID"),
                        Name = (string?)it.Element("Name") ?? (string?)it.Element("FullName"),
                        FullName = (string?)it.Element("FullName"),
                        EditSequence = (string?)it.Element("EditSequence"),
                        SalesPrice = TryDec((string?)it.Element("SalesPrice") ?? (string?)sop?.Element("Price")),
                        SalesDesc = (string?)it.Element("SalesDesc") ?? (string?)sop?.Element("SalesDesc"),
                        IsActive = TryBool((string?)it.Element("IsActive")),
                        TimeModified = TryDate((string?)it.Element("TimeModified")),
                        Type = "OtherCharge"
                    };

                var list = items.ToList();
                result.ServiceItems.AddRange(list);

                await _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: $"othercharge.items={list.Count}",
                    ct: ct);

                return result;
            }

            // ---------- ItemSalesTaxQueryRs ----------
            var taxRs = doc.Descendants("ItemSalesTaxQueryRs").FirstOrDefault();
            if (taxRs != null)
            {
                PopulateIterator(taxRs, result);

                var items =
                    from it in taxRs.Elements("ItemSalesTaxRet")
                    select new CatalogItemDto
                    {
                        ListID = (string?)it.Element("ListID"),
                        Name = (string?)it.Element("Name") ?? (string?)it.Element("FullName"),
                        FullName = (string?)it.Element("FullName"),
                        EditSequence = (string?)it.Element("EditSequence"),
                        SalesDesc = (string?)it.Element("ItemDesc"),
                        IsActive = TryBool((string?)it.Element("IsActive")),
                        TimeModified = TryDate((string?)it.Element("TimeModified")),
                        Type = "SalesTaxItem"
                    };

                var list = items.ToList();
                result.ServiceItems.AddRange(list);

                await _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: $"salestax.items={list.Count}",
                    ct: ct);

                return result;
            }

            // ---------- ItemSalesTaxGroupQueryRs ----------
            var taxGrpRs = doc.Descendants("ItemSalesTaxGroupQueryRs").FirstOrDefault();
            if (taxGrpRs != null)
            {
                PopulateIterator(taxGrpRs, result);

                var items =
                    from it in taxGrpRs.Elements("ItemSalesTaxGroupRet")
                    select new CatalogItemDto
                    {
                        ListID = (string?)it.Element("ListID"),
                        Name = (string?)it.Element("Name") ?? (string?)it.Element("FullName"),
                        FullName = (string?)it.Element("FullName"),
                        EditSequence = (string?)it.Element("EditSequence"),
                        SalesDesc = (string?)it.Element("ItemDesc"),
                        IsActive = TryBool((string?)it.Element("IsActive")),
                        TimeModified = TryDate((string?)it.Element("TimeModified")),
                        Type = "SalesTaxGroup"
                    };

                var list = items.ToList();
                result.ServiceItems.AddRange(list);

                await _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: $"salestaxgroup.items={list.Count}",
                    ct: ct);

                return result;
            }

            // ====================== NEW: INVOICE ADD RESPONSE ======================
            var invAddRs = doc.Descendants("InvoiceAddRs").FirstOrDefault();
            if (invAddRs != null)
            {
                PopulateIterator(invAddRs, result); // harmless here

                var ret = invAddRs.Element("InvoiceRet");
                if (ret != null)
                {
                    result.InvoiceTxnId = (string?)ret.Element("TxnID");
                    result.InvoiceEditSeq = (string?)ret.Element("EditSequence");
                }

                var scAttr = (string?)invAddRs.Attribute("statusCode");
                var smAttr = (string?)invAddRs.Attribute("statusMessage");
                if (int.TryParse(scAttr, out var sc)) result.StatusCode = sc;
                result.StatusMessage = smAttr ?? result.StatusMessage;

                await _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message: $"InvoiceAddRs: txnId={result.InvoiceTxnId ?? "<null>"}; editSeq={result.InvoiceEditSeq ?? "<null>"}; status={result.StatusCode}:{result.StatusMessage}",
                    ct: ct);

                return result;
            }

            // ---------- CUSTOMERS/JOBS BRANCH ----------
            var custRs = doc.Descendants("CustomerQueryRs").FirstOrDefault();
            if (custRs != null)
            {
                PopulateIterator(custRs, result);

                var customers =
                    from c in custRs.Elements("CustomerRet")
                    let parent = c.Element("ParentRef")
                    let bill = c.Element("BillAddress")
                    let job = c.Element("JobInfo")
                    let jobTypeRef = job?.Element("JobTypeRef")
                    select new CustomerData
                    {
                        CustomerID = 0,
                        ID = (string?)c.Element("ListID") ?? "",
                        ParentID = (string?)parent?.Element("ListID") ?? "",
                        Name = (string?)c.Element("Name") ?? "",
                        ParentName = (string?)parent?.Element("FullName") ?? "",
                        SubLevelId = TryInt((string?)c.Element("Sublevel")) ?? 0,
                        Company = (string?)c.Element("CompanyName") ?? "",
                        FullName = (string?)c.Element("FullName") ?? "",
                        FirstName = (string?)c.Element("FirstName") ?? "",
                        LastName = (string?)c.Element("LastName") ?? "",
                        AccountNumber = (string?)c.Element("AccountNumber") ?? "",
                        Phone = (string?)c.Element("Phone") ?? (string?)c.Element("Phone1") ?? "",
                        Email = (string?)c.Element("Email") ?? "",
                        Notes = (string?)c.Element("Notes") ?? "",
                        FullAddress = BuildAddress(bill) ?? "",
                        JobStatus = (string?)job?.Element("JobStatus") ?? "",
                        JobStartDate = (string?)job?.Element("JobStartDate") ?? "",
                        JobProjectedEndDate = (string?)job?.Element("JobProjectedEndDate") ?? "",
                        JobDescription = (string?)job?.Element("JobDesc") ?? "",
                        JobType = (string?)jobTypeRef?.Element("FullName") ?? "",
                        JobTypeId = (string?)jobTypeRef?.Element("ListID") ?? "",
                        Description = (string?)c.Element("Notes") ?? "",
                        IsActive = TryBool((string?)c.Element("IsActive")) ?? false,
                        EffectiveActive = false,
                        MaterializedPath = "",
                        PathIds = "",
                        Depth = 0,
                        RootId = 0,
                        EditSequence = (string?)c.Element("EditSequence") ?? "",
                        QBLastUpdated = TryDate((string?)c.Element("TimeModified")),
                        LastUpdated = DateTime.UtcNow,
                        UpdateType = "A",
                        ChangeVersion = "",
                        ParentCustomerID = 0,
                        UnitNumber = ""
                    };

                var list = customers.ToList();
                result.Customers.AddRange(list);

                await _audit.LogMessageAsync(
                    runId, "receiveResponseXML", "resp",
                    message:
                        $"customers.items={list.Count}; " +
                        $"withParents={list.Count(x => !string.IsNullOrEmpty(x.ParentID))}; " +
                        $"sublevels>0={list.Count(x => x.SubLevelId > 0)}; " +
                        $"active={list.Count(x => x.IsActive)}",
                    ct: ct);

                return result;
            }

            // Neither branch matched
            return result;
        }


        // ------------- helpers -------------
        private static void PopulateIterator(XElement rs, ReceiveParseResult result)
        {
            var iterRemainAttr = (string?)rs.Attribute("iteratorRemainingCount");
            var iterIdAttr = (string?)rs.Attribute("iteratorID");
            var statusCodeAttr = (string?)rs.Attribute("statusCode");
            var statusMsgAttr = (string?)rs.Attribute("statusMessage");

            if (int.TryParse(iterRemainAttr, out var remain)) result.IteratorRemaining = remain;
            result.IteratorId = iterIdAttr;
            if (int.TryParse(statusCodeAttr, out var sc)) result.StatusCode = sc;
            result.StatusMessage = statusMsgAttr;
        }

        private static string? BuildAddress(XElement? bill)
        {
            if (bill == null) return null;

            var parts = new[]
            {
                (string?)bill.Element("Addr1"),
                (string?)bill.Element("Addr2"),
                (string?)bill.Element("Addr3"),
                (string?)bill.Element("Addr4"),
                (string?)bill.Element("Addr5"),
                JoinCityStatePostal(
                    (string?)bill.Element("City"),
                    (string?)bill.Element("State"),
                    (string?)bill.Element("PostalCode")),
                (string?)bill.Element("Country")
            }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim());

            var line = string.Join(", ", parts);
            return string.IsNullOrWhiteSpace(line) ? null : line;
        }

        private static string? JoinCityStatePostal(string? city, string? state, string? postal)
        {
            var left = string.Join(", ",
                new[] { city, state }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim()));

            if (string.IsNullOrWhiteSpace(postal)) return string.IsNullOrWhiteSpace(left) ? null : left;
            return string.IsNullOrWhiteSpace(left) ? postal?.Trim() : $"{left} {postal!.Trim()}";
        }

        private static int? TryInt(string? s) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int?)null;

        private static bool? TryBool(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("1", StringComparison.OrdinalIgnoreCase);
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
