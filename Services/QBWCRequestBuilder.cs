using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;
using System.Data;
using System.Reflection.PortableExecutable;
using RaymarEquipmentInventory.Helpers;
using Serilog;
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
            "ListID","FullName","EditSequence","QuantityOnHand","SalesPrice","PurchaseCost"
        };

        public QbwcRequestBuilder(IOptions<QbwcRequestOptions> opt)
        {
            _opt = opt.Value ?? new QbwcRequestOptions();
        }

        public string BuildCompanyQuery() =>
                @"<?xml version=""1.0"" encoding=""utf-8""?>
                <QBXML>
                  <QBXMLMsgsRq onError=""stopOnError"">
                    <CompanyQueryRq/>
                  </QBXMLMsgsRq>
                </QBXML>";

        public string BuildItemInventoryStart(int pageSize, bool activeOnly, string? fromModifiedIso8601Utc, string[]? includeRetElements = null)
        {
            var active = activeOnly ? "<ActiveStatus>ActiveOnly</ActiveStatus>" : "";
            var from = !string.IsNullOrWhiteSpace(fromModifiedIso8601Utc)
                          ? $"<FromModifiedDate>{fromModifiedIso8601Utc}</FromModifiedDate>" : "";
            var include = string.Join("\n      ", (includeRetElements ?? DefaultInclude).Select(x => $"<IncludeRetElement>{x}</IncludeRetElement>"));
                                return
                    $@"<?xml version=""1.0"" encoding=""utf-8""?>
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

        public string BuildItemInventoryContinue(string iteratorId, int pageSize, string[]? includeRetElements = null)
        {
            var include = string.Join("\n      ", (includeRetElements ?? DefaultInclude).Select(x => $"<IncludeRetElement>{x}</IncludeRetElement>"));
                                return
                    $@"<?xml version=""1.0"" encoding=""utf-8""?>
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

