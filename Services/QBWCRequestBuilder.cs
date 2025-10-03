using Microsoft.Extensions.Options;
using System;
using System.Linq;

namespace RaymarEquipmentInventory.Services
{
    public sealed class QbwcRequestBuilder : IQBWCRequestBuilder
    {
        private readonly QbwcRequestOptions _opt;

        public QbwcRequestBuilder(IOptions<QbwcRequestOptions> opt)
        {
            _opt = opt.Value ?? new QbwcRequestOptions();
        }

        // IMPORTANT: no encoding attribute here; include qbXML PI
        private const string QbXmlHeader = @"<?xml version=""1.0"" ?>
<?qbxml version=""16.0""?>";

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

        // CustomerRet aggregate fields cover subfields used for CustomerBackup mapping
        private static readonly string[] CustomerInclude = new[]
        {
            "ListID",
            "Name",
            "FullName",
            "ParentRef",            // ParentRef.ListID / ParentRef.FullName
            "Sublevel",
            "CompanyName",
            "FirstName",
            "LastName",
            "AccountNumber",
            "Phone",
            "Email",
            "Notes",
            "BillAddress",          // Addr1..City..PostalCode..Country
            "JobInfo",              // JobStatus, JobStartDate, JobProjectedEnd, JobDesc, JobTypeRef
            "IsActive",
            "TimeModified"
        };

        private static string IncludeBlock(string[]? include) =>
            string.Join("\n      ", (include ?? Array.Empty<string>()).Select(x => $"<IncludeRetElement>{x}</IncludeRetElement>"));

        // ---------- Company ----------
        public string BuildCompanyQuery() =>
$@"{QbXmlHeader}
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
$@"{QbXmlHeader}
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
$@"{QbXmlHeader}
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
$@"{QbXmlHeader}
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
$@"{QbXmlHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <CustomerQueryRq requestID=""cust-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </CustomerQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        // ---------- ItemService (Service items) ----------
        // Keep your existing include for reference; QB ignores IncludeRetElement for ItemService sometimes.
        private static readonly string[] ServiceInclude = new[]
        {
            "ListID",
            "Name",
            "FullName",
            "EditSequence",
            "SalesPrice",
            "PurchaseCost",
            "SalesDesc",
            "PurchaseDesc",
            "IsActive",
            "TimeModified"
        };

        public string BuildItemServiceStart(int pageSize, bool activeOnly, string? fromIso)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromIso) ? $"<FromModifiedDate>{fromIso}</FromModifiedDate>" : "";

            return $@"{QbXmlHeader}
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
            return $@"{QbXmlHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemServiceQueryRq requestID=""svc-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
    </ItemServiceQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        // =====================================================================
        // NEW: Non-Inventory, Other Charge, Sales Tax Item, Sales Tax Group
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
            // (Group members come as ItemSalesTaxRef aggregate; we’ll parse in handler)
        };

        // ---- ItemNonInventory ----
        public string BuildItemNonInventoryStart(int pageSize, bool activeOnly, string? fromIso)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromIso) ? $"<FromModifiedDate>{fromIso}</FromModifiedDate>" : "";
            var include = IncludeBlock(NonInventoryInclude);

            return $@"{QbXmlHeader}
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
            return $@"{QbXmlHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemNonInventoryQueryRq requestID=""noninv-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemNonInventoryQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        // ---- ItemOtherCharge ----
        public string BuildItemOtherChargeStart(int pageSize, bool activeOnly, string? fromIso)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromIso) ? $"<FromModifiedDate>{fromIso}</FromModifiedDate>" : "";
            var include = IncludeBlock(OtherChargeInclude);

            return $@"{QbXmlHeader}
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
            return $@"{QbXmlHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemOtherChargeQueryRq requestID=""other-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemOtherChargeQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        // ---- ItemSalesTax (single tax item, e.g., HST) ----
        public string BuildItemSalesTaxStart(int pageSize, bool activeOnly, string? fromIso)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromIso) ? $"<FromModifiedDate>{fromIso}</FromModifiedDate>" : "";
            var include = IncludeBlock(SalesTaxItemInclude);

            return $@"{QbXmlHeader}
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
            return $@"{QbXmlHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemSalesTaxQueryRq requestID=""tax-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemSalesTaxQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }

        // ---- ItemSalesTaxGroup (group like HST composed of members) ----
        public string BuildItemSalesTaxGroupStart(int pageSize, bool activeOnly, string? fromIso)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromIso) ? $"<FromModifiedDate>{fromIso}</FromModifiedDate>" : "";
            var include = IncludeBlock(SalesTaxGroupInclude);

            return $@"{QbXmlHeader}
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
            return $@"{QbXmlHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <ItemSalesTaxGroupQueryRq requestID=""taxgrp-1"" iterator=""Continue"" iteratorID=""{System.Security.SecurityElement.Escape(iteratorId)}"">
      <MaxReturned>{pageSize}</MaxReturned>
      {include}
    </ItemSalesTaxGroupQueryRq>
  </QBXMLMsgsRq>
</QBXML>";
        }
    }
}
