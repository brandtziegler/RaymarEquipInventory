using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public sealed class InventoryImportService : IInventoryImportService
    {
        private readonly RaymarInventoryDBContext _context;
        private readonly IAuditLogger _audit;

        public InventoryImportService(RaymarInventoryDBContext context, IAuditLogger audit)
        {
            _context = context;
            _audit = audit;
        }

       
        public async Task<int> BulkInsertInventoryAsync(
            Guid runId,
            IEnumerable<InventoryItemDto> items,
            CancellationToken ct = default)
        {
            var list = items as List<InventoryItemDto> ?? new List<InventoryItemDto>(items);
            if (list.Count == 0)
            {
                await _audit.LogMessageAsync(runId, "InventoryStaging", "resp",
                    message: "0 rows (empty batch)", ct: ct);
                return 0;
            }

            var table = BuildTable(runId, list);
          
            var conn = (SqlConnection)_context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            using var bulk = new SqlBulkCopy(conn)
            {
                DestinationTableName = "dbo.InventoryStaging",
                BulkCopyTimeout = 0,
                BatchSize = Math.Min(1000, table.Rows.Count)
            };

            // Existing mappings
            bulk.ColumnMappings.Add("RunId", "RunId");
            bulk.ColumnMappings.Add("ListID", "ListID");
            bulk.ColumnMappings.Add("FullName", "FullName");
            bulk.ColumnMappings.Add("EditSequence", "EditSequence");
            bulk.ColumnMappings.Add("QuantityOnHand", "QuantityOnHand");
            bulk.ColumnMappings.Add("SalesPrice", "SalesPrice");
            bulk.ColumnMappings.Add("PurchaseCost", "PurchaseCost");
            bulk.ColumnMappings.Add("CreatedAtUtc", "CreatedAtUtc");

            // New mappings
            bulk.ColumnMappings.Add("Name", "Name");
            bulk.ColumnMappings.Add("SalesDesc", "SalesDesc");
            bulk.ColumnMappings.Add("PurchaseDesc", "PurchaseDesc");
            bulk.ColumnMappings.Add("ManufacturerPartNum", "ManufacturerPartNum");
            bulk.ColumnMappings.Add("TimeModified", "TimeModified");

            await bulk.WriteToServerAsync(table, ct);

            await _audit.LogMessageAsync(runId, "InventoryStaging", "resp",
                message: $"Inserted {table.Rows.Count} rows", ct: ct);

            return table.Rows.Count;
        }


        /// <summary>
        /// Promote deltas from vw_InventoryDelta into InventoryDataBackup (idempotent).
        /// - NEW      -> INSERT (IsActive=1, LastTrans='I')
        /// - CHANGED  -> UPDATE (IsActive=1, LastTrans='U')
        /// - DELETED  -> soft delete (IsActive=0, LastTrans='D')
        /// </summary>
        public async Task<InventoryBackupSyncResult> SyncInventoryDataAsync(
            Guid runId,
            CancellationToken ct = default)
        {
            var conn = (SqlConnection)_context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            using var tx = await _context.Database.BeginTransactionAsync(ct);

            // 1) UPDATE changed
            var sqlUpdateChanged = @"
UPDATE b
SET  b.ManufacturerPartNumber = LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(v.Staging_MPN,CHAR(160),' '),CHAR(9),' '),CHAR(13),' '))),
     b.[Description] = LEFT(CONCAT(
        COALESCE(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(v.Staging_MPN,CHAR(160),' '),CHAR(9),' '),CHAR(13),' '))), ''),
        CASE WHEN NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(v.Staging_MPN,CHAR(160),' '),CHAR(9),' '),CHAR(13),' '))), '') IS NOT NULL
              AND NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(v.Staging_Desc,CHAR(160),' '),CHAR(9),' '),CHAR(13),' '))), '') IS NOT NULL
             THEN '-' ELSE '' END,
        COALESCE(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(v.Staging_Desc,CHAR(160),' '),CHAR(9),' '),CHAR(13),' '))), '')
     ),255),
     b.Cost = v.Staging_Cost,
     b.SalesPrice = v.Staging_SalesPrice,
     b.OnHand = v.Staging_OnHand,
     b.IsActive = 1,
     b.LastUpdated = SYSUTCDATETIME(),
     b.LastTrans = 'U'
FROM dbo.InventoryData b
JOIN dbo.vw_InventoryDelta v
  ON TRIM(v.ListID)=TRIM(b.QuickBooksInvID)
WHERE v.Status='CHANGED';
";
            var updated = await _context.Database.ExecuteSqlRawAsync(sqlUpdateChanged);

            // 2) INSERT new (normalized exactly like UPDATE)
            var sqlInsertNew = @"
INSERT dbo.InventoryData (
  ItemName, ManufacturerPartNumber, [Description],
  Cost, SalesPrice, ReorderPoint, OnHand, AverageCost, IncomeAccountID,
  QuickBooksInvID, LastRestockedDate, IsActive, LastUpdated, LastTrans
)
SELECT
  NULL,
  LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(v.Staging_MPN,CHAR(160),' '),CHAR(9),' '),CHAR(13),' '))),
  LEFT(CONCAT(
      COALESCE(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(v.Staging_MPN,CHAR(160),' '),CHAR(9),' '),CHAR(13),' '))), ''),
      CASE WHEN NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(v.Staging_MPN,CHAR(160),' '),CHAR(9),' '),CHAR(13),' '))), '') IS NOT NULL
            AND NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(v.Staging_Desc,CHAR(160),' '),CHAR(9),' '),CHAR(13),' '))), '') IS NOT NULL
           THEN '-' ELSE '' END,
      COALESCE(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(v.Staging_Desc,CHAR(160),' '),CHAR(9),' '),CHAR(13),' '))), '')
  ),255),
  v.Staging_Cost, v.Staging_SalesPrice, NULL, v.Staging_OnHand,
  NULL, NULL, v.ListID, NULL, 1, SYSUTCDATETIME(), 'I'
FROM dbo.vw_InventoryDelta v
LEFT JOIN dbo.InventoryData b
  ON TRIM(b.QuickBooksInvID)=TRIM(v.ListID)
WHERE v.Status='NEW' AND b.InventoryID IS NULL;
";
            var inserted = await _context.Database.ExecuteSqlRawAsync(sqlInsertNew);

            // 3) Soft-delete those marked DELETED (protect special item)
            var sqlSoftDelete = @"
UPDATE b
SET  b.IsActive    = 0,
     b.LastUpdated = SYSUTCDATETIME(),
     b.LastTrans   = 'D'
FROM dbo.InventoryData b
JOIN dbo.vw_InventoryDelta v
  ON TRIM(v.ListID) = TRIM(b.QuickBooksInvID)
WHERE v.Status = 'DELETED'
  AND b.IsActive = 1
  AND ISNULL(b.ManufacturerPartNumber,'') <> '__TRAVEL_RECEIPT__';
";
            var deactivated = await _context.Database.ExecuteSqlRawAsync(sqlSoftDelete);

            await tx.CommitAsync(ct);

            await _audit.LogMessageAsync(
                runId, "InventoryData", "promote",
                message: $"Inserted={inserted}, Updated={updated}, Deactivated={deactivated}", ct: ct);

            return new InventoryBackupSyncResult
            {
                Inserted = inserted,
                Updated = updated,
                Deactivated = deactivated
            };
        }


        private static DataTable BuildTable(Guid runId, List<InventoryItemDto> items)
        {
            var dt = new DataTable("InventoryStaging");

            // existing columns
            dt.Columns.Add(new DataColumn("RunId", typeof(Guid)));
            dt.Columns.Add(new DataColumn("ListID", typeof(string)) { MaxLength = 50 });      // nvarchar(50)  NOT NULL
            dt.Columns.Add(new DataColumn("FullName", typeof(string)) { MaxLength = 300 });   // nvarchar(300)
            dt.Columns.Add(new DataColumn("EditSequence", typeof(string)) { MaxLength = 50 }); // nvarchar(50)
            dt.Columns.Add(new DataColumn("QuantityOnHand", typeof(decimal)));                // decimal(18,4)
            dt.Columns.Add(new DataColumn("SalesPrice", typeof(decimal)));                    // decimal(18,4)
            dt.Columns.Add(new DataColumn("PurchaseCost", typeof(decimal)));                  // decimal(18,4)
            dt.Columns.Add(new DataColumn("CreatedAtUtc", typeof(DateTime)));

            // new columns
            dt.Columns.Add(new DataColumn("Name", typeof(string)) { MaxLength = 100 });       // nvarchar(100)
            dt.Columns.Add(new DataColumn("SalesDesc", typeof(string)) { MaxLength = 4000 }); // nvarchar(4000)
            dt.Columns.Add(new DataColumn("PurchaseDesc", typeof(string)) { MaxLength = 4000 }); // nvarchar(4000)
            dt.Columns.Add(new DataColumn("ManufacturerPartNum", typeof(string)) { MaxLength = 100 }); // nvarchar(100)
            dt.Columns.Add(new DataColumn("TimeModified", typeof(DateTime)));                 // datetime2(3) NULL

            var now = DateTime.UtcNow;

            foreach (var x in items)
            {
                var row = dt.NewRow();

                row["RunId"] = runId;
                row["ListID"] = Trunc(x.ListID, 50)!;                 // NOT NULL
                row["FullName"] = (object?)Trunc(x.FullName, 300) ?? DBNull.Value;
                row["EditSequence"] = (object?)Trunc(x.EditSequence, 50) ?? DBNull.Value;

                row["QuantityOnHand"] = (object)Round4(x.QuantityOnHand) ?? 0m; // NOT NULL in SQL
                row["SalesPrice"] = (object?)Round4(x.SalesPrice) ?? DBNull.Value;
                row["PurchaseCost"] = (object?)Round4(x.PurchaseCost) ?? DBNull.Value;

                row["CreatedAtUtc"] = now;

                row["Name"] = (object?)Trunc(x.Name, 100) ?? DBNull.Value;
                row["SalesDesc"] = (object?)Trunc(x.SalesDesc, 4000) ?? DBNull.Value;
                row["PurchaseDesc"] = (object?)Trunc(x.PurchaseDesc, 4000) ?? DBNull.Value;
                row["ManufacturerPartNum"] = (object?)Trunc(x.ManufacturerPartNum, 100) ?? DBNull.Value;

                row["TimeModified"] = (object?)x.TimeModified ?? DBNull.Value;

                dt.Rows.Add(row);
            }

            return dt;

            static decimal? Round4(decimal? v) =>
                v.HasValue ? Math.Round(v.Value, 4, MidpointRounding.AwayFromZero) : (decimal?)null;

            static string? Trunc(string? s, int len) =>
                string.IsNullOrEmpty(s) ? s : (s.Length <= len ? s : s.Substring(0, len));
        }
    }
}


