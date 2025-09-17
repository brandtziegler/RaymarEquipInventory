using Microsoft.Extensions.Options;
using System;
using System.Linq;

namespace RaymarEquipmentInventory.Services
{
    public sealed class QbwcRequestBuilder : IQBWCRequestBuilder
    {
        private readonly QbwcRequestOptions _opt;

        private static readonly string[] DefaultInclude = new[]
         {
                "ListID",
                "Name",                 // add
                "FullName",
                "EditSequence",
                "QuantityOnHand",
                "SalesPrice",
                "PurchaseCost",
                "SalesDesc",            // add
                "PurchaseDesc",         // add
                "ManufacturerPartNumber", // add
                "TimeModified"
        };

        public QbwcRequestBuilder(IOptions<QbwcRequestOptions> opt)
        {
            _opt = opt.Value ?? new QbwcRequestOptions();
        }

        // IMPORTANT: no encoding attribute here; include qbXML PI
        private const string QbXmlHeader = @"<?xml version=""1.0"" ?>
<?qbxml version=""16.0""?>";

        public string BuildCompanyQuery() =>
$@"{QbXmlHeader}
<QBXML>
  <QBXMLMsgsRq onError=""stopOnError"">
    <CompanyQueryRq/>
  </QBXMLMsgsRq>
</QBXML>";

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
            var include = string.Join("\n      ",
                (includeRetElements ?? DefaultInclude)
                .Select(x => $"<IncludeRetElement>{x}</IncludeRetElement>"));

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
            var include = string.Join("\n      ",
                (includeRetElements ?? DefaultInclude)
                .Select(x => $"<IncludeRetElement>{x}</IncludeRetElement>"));

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
    }
}


