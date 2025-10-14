using Microsoft.Extensions.Options;
using RaymarEquipmentInventory.DTOs;
using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RaymarEquipmentInventory.Services
{
    public sealed class QbwcRequestBuilder : IQBWCRequestBuilder
    {
        private readonly QbwcRequestOptions _opt;
        private readonly int _defaultMajor;
        private readonly int _defaultMinor;

        public QbwcRequestBuilder(IOptions<QbwcRequestOptions> opt)
        {
            _opt = opt.Value ?? new QbwcRequestOptions();
            // If you ever add these to options, wire them here; else default to a widely-supported version.
            _defaultMajor = 14;
            _defaultMinor = 0;
        }

        // Build a header with *no* leading whitespace and *no* extra spaces in the PI.
        private static string Header(int major, int minor) =>
            $@"<?xml version=""1.0""?>{Environment.NewLine}<?qbxml version=""{major}.{minor}""?>";

        // Convenience wrapper for default header
        private string DefaultHeader => Header(_defaultMajor, _defaultMinor);

        // ---------- Include sets ----------
        private static readonly string[] ItemInclude = new[]
        {
            "ListID",
            "Name",
            "FullName",
            "EditSequence",
            "QuantityOnHand",
            "SalesPrice",
            "PurchaseCost",
            "SalesDesc",
            "PurchaseDesc",
            "ManufacturerPartNumber",
            "TimeModified"
        };

        private static readonly string[] CustomerInclude = new[]
        {
            "ListID",
            "Name",
            "FullName",
            "ParentRef",
            "Sublevel",
            "CompanyName",
            "FirstName",
            "LastName",
            "AccountNumber",
            "Phone",
            "Email",
            "Notes",
            "BillAddress",
            "JobInfo",
            "IsActive",
            "TimeModified"
        };

        private static string IncludeBlock(string[]? include) =>
            string.Join("\n      ", (include ?? Array.Empty<string>()).Select(x => $"<IncludeRetElement>{x}</IncludeRetElement>"));

        // ---------- Company ----------
        public string BuildCompanyQuery() =>
$@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <CompanyQueryRq/>
  </QBXMLMsgsRq>
</QBXML>";

        // ---------- Inventory (ItemInventory) ----------
        public string BuildItemInventoryStart(
            int pageSize,
            bool activeOnly,
            string? fromModifiedIso8601Utc,
            string[]? includeRetElements = null)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromModifiedIso8601Utc)
                       ? $"<FromModifiedDate>{fromModifiedIso8601Utc}</FromModifiedDate>"
                       : "";
            var include = IncludeBlock(includeRetElements ?? ItemInclude);

            return
$@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemInventoryQueryRq requestID=""inv-1"" iterator=""Start"">
      {active}
      {from}
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemInventoryQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        public string BuildItemInventoryContinue(
            string iteratorId,
            int pageSize,
            string[]? includeRetElements = null)
        {
            var include = IncludeBlock(includeRetElements ?? ItemInclude);

            return
$@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemInventoryQueryRq requestID=""inv-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemInventoryQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        // ---------- Customers / Jobs (CustomerRet) ----------
        public string BuildCustomerStart(
            int pageSize,
            bool activeOnly,
            string? fromModifiedIso8601Utc,
            string[]? includeRetElements = null)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromModifiedIso8601Utc)
                       ? $"<FromModifiedDate>{fromModifiedIso8601Utc}</FromModifiedDate>"
                       : "";
            var include = IncludeBlock(includeRetElements ?? CustomerInclude);

            return
$@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <CustomerQueryRq requestID=""cust-1"" iterator=""Start"">
      {active}
      {from}
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </CustomerQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        public string BuildCustomerContinue(
            string iteratorId,
            int pageSize,
            string[]? includeRetElements = null)
        {
            var include = IncludeBlock(includeRetElements ?? CustomerInclude);

            return
$@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <CustomerQueryRq requestID=""cust-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </CustomerQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        // ---------- ItemService ----------
        private static readonly string[] ServiceInclude = new[]
        {
            "ListID","Name","FullName","EditSequence",
            "SalesPrice","PurchaseCost","SalesDesc","PurchaseDesc",
            "IsActive","TimeModified"
        };

        public string BuildItemServiceStart(int pageSize, bool activeOnly, string? fromIso)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromIso) ? $"<FromModifiedDate>{fromIso}</FromModifiedDate>" : "";

            return $@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemServiceQueryRq requestID=""svc-1"" iterator=""Start"">
      {active}
      {from}
      <MaxReturned>{pageSize}</MaxReturned>
    </ItemServiceQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        public string BuildItemServiceContinue(string iteratorId, int pageSize)
        {
            return $@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemServiceQueryRq requestID=""svc-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
    </ItemServiceQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        // =====================================================================
        // Non-Inventory, Other Charge, Sales Tax Item, Sales Tax Group
        // =====================================================================

        private static readonly string[] NonInventoryInclude = new[]
        {
            "ListID","Name","FullName","EditSequence",
            "SalesPrice","PurchaseCost","SalesDesc","PurchaseDesc",
            "IsActive","TimeModified"
        };

        private static readonly string[] OtherChargeInclude = new[]
        {
            "ListID","Name","FullName","EditSequence",
            "SalesPrice","SalesDesc","IsActive","TimeModified"
        };

        private static readonly string[] SalesTaxItemInclude = new[]
        {
            "ListID","Name","FullName","EditSequence",
            "ItemDesc","TaxRate","IsActive","TimeModified"
        };

        private static readonly string[] SalesTaxGroupInclude = new[]
        {
            "ListID","Name","FullName","EditSequence",
            "ItemDesc","IsActive","TimeModified"
        };

        public string BuildItemNonInventoryStart(int pageSize, bool activeOnly, string? fromIso)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromIso) ? $"<FromModifiedDate>{fromIso}</FromModifiedDate>" : "";
            var include = IncludeBlock(NonInventoryInclude);

            return $@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemNonInventoryQueryRq requestID=""noninv-1"" iterator=""Start"">
      {active}
      {from}
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemNonInventoryQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        public string BuildItemNonInventoryContinue(string iteratorId, int pageSize)
        {
            var include = IncludeBlock(NonInventoryInclude);
            return $@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemNonInventoryQueryRq requestID=""noninv-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemNonInventoryQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        public string BuildItemOtherChargeStart(int pageSize, bool activeOnly, string? fromIso)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromIso) ? $"<FromModifiedDate>{fromIso}</FromModifiedDate>" : "";
            var include = IncludeBlock(OtherChargeInclude);

            return $@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemOtherChargeQueryRq requestID=""other-1"" iterator=""Start"">
      {active}
      {from}
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemOtherChargeQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        public string BuildItemOtherChargeContinue(string iteratorId, int pageSize)
        {
            var include = IncludeBlock(OtherChargeInclude);
            return $@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemOtherChargeQueryRq requestID=""other-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemOtherChargeQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        public string BuildItemSalesTaxStart(int pageSize, bool activeOnly, string? fromIso)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromIso) ? $"<FromModifiedDate>{fromIso}</FromModifiedDate>" : "";
            var include = IncludeBlock(SalesTaxItemInclude);

            return $@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemSalesTaxQueryRq requestID=""tax-1"" iterator=""Start"">
      {active}
      {from}
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemSalesTaxQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        public string BuildItemSalesTaxContinue(string iteratorId, int pageSize)
        {
            var include = IncludeBlock(SalesTaxItemInclude);
            return $@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemSalesTaxQueryRq requestID=""tax-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemSalesTaxQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        public string BuildItemSalesTaxGroupStart(int pageSize, bool activeOnly, string? fromIso)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromIso) ? $"<FromModifiedDate>{fromIso}</FromModifiedDate>" : "";
            var include = IncludeBlock(SalesTaxGroupInclude);

            return $@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemSalesTaxGroupQueryRq requestID=""taxgrp-1"" iterator=""Start"">
      {active}
      {from}
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemSalesTaxGroupQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        public string BuildItemSalesTaxGroupContinue(string iteratorId, int pageSize)
        {
            var include = IncludeBlock(SalesTaxGroupInclude);
            return $@"{DefaultHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemSalesTaxGroupQueryRq requestID=""taxgrp-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemSalesTaxGroupQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        // =====================================================================
        // INVOICE ADD
        // =====================================================================
        // Sanitizer for QuickBooks-safe XML text
        private static string CleanForQBXML(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var sb = new StringBuilder(input.Length);
            foreach (var c in input)
            {
                // Skip illegal XML control chars (except tab/newline)
                if (c < 0x20 && c != 0x09 && c != 0x0A && c != 0x0D)
                    continue;

                // Replace non-ASCII characters with '?'
                if (c > 0x7E)
                    sb.Append('?');
                else
                    sb.Append(c);
            }

            return System.Security.SecurityElement.Escape(sb.ToString());
        }



        public string BuildInvoiceAdd(InvoiceAddPayload p, int qbXmlMajor, int qbXmlMinor, string qbXmlCountry = "US")
        {
            // Lock to stable version
            qbXmlMajor = 14;
            qbXmlMinor = 0;

            string D(DateTime dt) => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string N(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

            const string header = "<?xml version=\"1.0\"?>\n<?qbxml version=\"14.0\"?>";

            var sb = new StringBuilder(4096);
            sb.Append(header);
            sb.Append("<QBXML><QBXMLMsgsRq onError=\"stopOnError\">");
            sb.Append(@"<InvoiceAddRq requestID=""inv-add-1""><InvoiceAdd>");

            sb.Append($@"<CustomerRef><ListID>{CleanForQBXML(p.CustomerListID)}</ListID></CustomerRef>");

            if (!string.IsNullOrWhiteSpace(p.ItemSalesTaxRefListID))
                sb.Append($@"<ItemSalesTaxRef><ListID>{CleanForQBXML(p.ItemSalesTaxRefListID)}</ListID></ItemSalesTaxRef>");

            sb.Append($@"<RefNumber>{CleanForQBXML(p.RefNumber)}</RefNumber>");
            sb.Append($@"<TxnDate>{D(p.TxnDate)}</TxnDate>");

            if (!string.IsNullOrWhiteSpace(p.PONumber))
                sb.Append($@"<PONumber>{CleanForQBXML(p.PONumber)}</PONumber>");

            if (!string.IsNullOrWhiteSpace(p.Memo))
                sb.Append($@"<Memo>{CleanForQBXML(p.Memo)}</Memo>");

            foreach (var L in p.Lines)
            {
                sb.Append("<InvoiceLineAdd>");

                if (!string.IsNullOrWhiteSpace(L.ItemListID))
                    sb.Append($@"<ItemRef><ListID>{CleanForQBXML(L.ItemListID)}</ListID></ItemRef>");

                if (!string.IsNullOrWhiteSpace(L.Desc))
                    sb.Append($@"<Desc>{CleanForQBXML(L.Desc)}</Desc>");

                if (L.ServiceDate.HasValue)
                    sb.Append($@"<ServiceDate>{D(L.ServiceDate.Value)}</ServiceDate>");

                if (!string.IsNullOrWhiteSpace(L.ClassRef))
                    sb.Append($@"<ClassRef><FullName>{CleanForQBXML(L.ClassRef)}</FullName></ClassRef>");

                if (L.Qty.HasValue)
                    sb.Append($@"<Quantity>{N(L.Qty.Value)}</Quantity>");

                if (L.Rate.HasValue)
                    sb.Append($@"<Rate>{N(L.Rate.Value)}</Rate>");

                if (L.IsTaxable.HasValue)
                    sb.Append($@"<SalesTaxCodeRef><FullName>{(L.IsTaxable.Value ? "Tax" : "Non")}</FullName></SalesTaxCodeRef>");

                sb.Append("</InvoiceLineAdd>");
            }

            sb.Append("</InvoiceAdd></InvoiceAddRq></QBXMLMsgsRq></QBXML>");
            return sb.ToString();
        }

        // Default overload
        public string BuildInvoiceAdd(InvoiceAddPayload p)
            => BuildInvoiceAdd(p, _defaultMajor, _defaultMinor, "US");
    }
}
