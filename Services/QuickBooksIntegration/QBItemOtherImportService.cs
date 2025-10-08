using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    /// Bulk-copies NonInventory / OtherCharge / SalesTaxItem / SalesTaxGroup rows into dbo.QBItemOther_Staging
    public sealed class QBItemOtherImportService : IQBItemOtherImportService
    {
        private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "NonInventory","OtherCharge","SalesTaxItem","SalesTaxGroup"
        };

        private readonly RaymarInventoryDBContext _context;
        private readonly IAuditLogger _audit;

        public QBItemOtherImportService(RaymarInventoryDBContext context, IAuditLogger audit)
        {
            _context = context;
            _audit = audit;
        }

        public async Task<int> BulkInsertOtherItemsAsync(Guid runId, IEnumerable<CatalogItemDto> items, bool firstPage = false, CancellationToken ct = default)
        {

            if (firstPage)
            {
                await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE dbo.QBItemOther_Staging;", ct);
                await _audit.LogMessageAsync(runId, "QBItemOther_Staging", "resp",
                    message: "TRUNCATE on first page of OtherItems import", ct: ct);
            }

            var list = (items ?? Enumerable.Empty<CatalogItemDto>())
                      .Where(x => x != null && !string.IsNullOrWhiteSpace(x.ListID) && AllowedTypes.Contains(x.Type ?? ""))
                      .ToList();

            if (list.Count == 0)
            {
                await _audit.LogMessageAsync(runId, "QBItemOther_Staging", "resp", message: "0 rows (empty batch)", ct: ct);
                return 0;
            }

            var table = BuildStagingTable(runId, list);

            var conn = (SqlConnection)_context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            using var bulk = new SqlBulkCopy(conn)
            {
                DestinationTableName = "dbo.QBItemOther_Staging",
                BulkCopyTimeout = 0,
                BatchSize = Math.Min(1000, table.Rows.Count)
            };

            bulk.ColumnMappings.Add("RunId", "RunId");
            bulk.ColumnMappings.Add("ListID", "ListID");
            bulk.ColumnMappings.Add("Type", "Type");
            bulk.ColumnMappings.Add("Name", "Name");
            bulk.ColumnMappings.Add("FullName", "FullName");
            bulk.ColumnMappings.Add("SalesDesc", "SalesDesc");
            bulk.ColumnMappings.Add("PurchaseDesc", "PurchaseDesc");
            bulk.ColumnMappings.Add("SalesPrice", "SalesPrice");
            bulk.ColumnMappings.Add("PurchaseCost", "PurchaseCost");
            bulk.ColumnMappings.Add("IsActive", "IsActive");
            bulk.ColumnMappings.Add("TimeModified", "TimeModified");
            bulk.ColumnMappings.Add("CreatedAtUtc", "CreatedAtUtc");

            await bulk.WriteToServerAsync(table, ct);

            // small per-type summary for logs
            var summary = list
                .GroupBy(x => x.Type ?? "Unknown")
                .Select(g => $"{g.Key}={g.Count()}")
                .ToList();

            await _audit.LogMessageAsync(
                runId, "QBItemOther_Staging", "resp",
                message: $"Inserted {table.Rows.Count} rows; " + string.Join(", ", summary),
                ct: ct);

            return table.Rows.Count;
        }

        private static DataTable BuildStagingTable(Guid runId, List<CatalogItemDto> items)
        {
            var dt = new DataTable("QBItemOther_Staging");
            dt.Columns.Add(new DataColumn("RunId", typeof(Guid)));
            dt.Columns.Add(new DataColumn("ListID", typeof(string)) { MaxLength = 50 });
            dt.Columns.Add(new DataColumn("Type", typeof(string)) { MaxLength = 32 });
            dt.Columns.Add(new DataColumn("Name", typeof(string)) { MaxLength = 300 });
            dt.Columns.Add(new DataColumn("FullName", typeof(string)) { MaxLength = 300 });
            dt.Columns.Add(new DataColumn("SalesDesc", typeof(string)) { MaxLength = 4000 });
            dt.Columns.Add(new DataColumn("PurchaseDesc", typeof(string)) { MaxLength = 4000 });
            dt.Columns.Add(new DataColumn("SalesPrice", typeof(decimal)));
            dt.Columns.Add(new DataColumn("PurchaseCost", typeof(decimal)));
            dt.Columns.Add(new DataColumn("IsActive", typeof(bool)));
            dt.Columns.Add(new DataColumn("TimeModified", typeof(DateTime)));
            dt.Columns.Add(new DataColumn("CreatedAtUtc", typeof(DateTime)));

            var now = DateTime.UtcNow;
            foreach (var x in items)
            {
                var r = dt.NewRow();
                r["RunId"] = runId;
                r["ListID"] = x.ListID ?? "";
                r["Type"] = x.Type ?? "";
                r["Name"] = (object?)x.Name ?? DBNull.Value;
                r["FullName"] = (object?)x.FullName ?? DBNull.Value;
                r["SalesDesc"] = (object?)x.SalesDesc ?? DBNull.Value;
                r["PurchaseDesc"] = (object?)x.PurchaseDesc ?? DBNull.Value;
                r["SalesPrice"] = (object?)x.SalesPrice ?? DBNull.Value;
                r["PurchaseCost"] = (object?)x.PurchaseCost ?? DBNull.Value;
                r["IsActive"] = (object?)x.IsActive ?? DBNull.Value;
                r["TimeModified"] = (object?)x.TimeModified ?? DBNull.Value;
                r["CreatedAtUtc"] = now;
                dt.Rows.Add(r);
            }
            return dt;
        }
    }
}
