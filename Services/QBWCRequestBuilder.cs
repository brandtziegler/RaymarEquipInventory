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
    }
}
