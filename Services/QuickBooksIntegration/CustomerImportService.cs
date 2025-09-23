using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public sealed class CustomerImportService : ICustomerImportService
    {
        private readonly RaymarInventoryDBContext _context;
        private readonly IAuditLogger _audit;

        public CustomerImportService(RaymarInventoryDBContext context, IAuditLogger audit)
        {
            _context = context;
            _audit = audit;
        }

        public async Task<int> BulkInsertCustomersAsync(
            Guid runId,
            IEnumerable<CustomerData> customers,
            CancellationToken ct = default)
        {
            var list = customers as List<CustomerData> ?? customers.ToList();
            if (list.Count == 0)
            {
                await _audit.LogMessageAsync(runId, "CustomerBackup", "resp",
                    message: "0 rows (empty batch)", ct: ct);
                return 0;
            }

            var table = BuildTable(runId, list);
            var conn = (SqlConnection)_context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            // Clear backup for pilot runs (simple, deterministic)
            await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE dbo.CustomerBackup;", ct);

            using var bulk = new SqlBulkCopy(conn)
            {
                DestinationTableName = "dbo.CustomerBackup",
                BulkCopyTimeout = 0,
                BatchSize = Math.Min(1000, table.Rows.Count)
            };

            // Do NOT map CustomerID (IDENTITY) or ChangeVersion (rowversion).
            bulk.ColumnMappings.Add("ID", "ID");
            bulk.ColumnMappings.Add("ParentID", "ParentID");
            bulk.ColumnMappings.Add("CustomerName", "CustomerName");
            bulk.ColumnMappings.Add("ParentName", "ParentName");
            bulk.ColumnMappings.Add("Company", "Company");
            bulk.ColumnMappings.Add("FullName", "FullName");
            bulk.ColumnMappings.Add("FirstName", "FirstName");
            bulk.ColumnMappings.Add("LastName", "LastName");
            bulk.ColumnMappings.Add("AccountNumber", "AccountNumber");
            bulk.ColumnMappings.Add("Phone", "Phone");
            bulk.ColumnMappings.Add("Email", "Email");
            bulk.ColumnMappings.Add("Notes", "Notes");
            bulk.ColumnMappings.Add("JobStatus", "JobStatus");
            bulk.ColumnMappings.Add("JobStartDate", "JobStartDate");
            bulk.ColumnMappings.Add("JobProjectedEndDate", "JobProjectedEndDate");
            bulk.ColumnMappings.Add("JobDescription", "JobDescription");
            bulk.ColumnMappings.Add("JobType", "JobType");
            bulk.ColumnMappings.Add("JobTypeId", "JobTypeId");
            bulk.ColumnMappings.Add("Description", "Description");
            bulk.ColumnMappings.Add("SubLevelId", "SubLevelId");
            bulk.ColumnMappings.Add("FullAddress", "FullAddress");
            bulk.ColumnMappings.Add("LastUpdated", "LastUpdated");
            bulk.ColumnMappings.Add("UpdateType", "UpdateType");
            bulk.ColumnMappings.Add("MaterializedPath", "MaterializedPath");
            bulk.ColumnMappings.Add("PathIds", "PathIds");
            bulk.ColumnMappings.Add("Depth", "Depth");
            bulk.ColumnMappings.Add("RootId", "RootId");
            bulk.ColumnMappings.Add("QbLastUpdated", "QBLastUpdated");      // if your column is QBlastUpdated
            bulk.ColumnMappings.Add("IsActive", "IsActive");
            bulk.ColumnMappings.Add("ServerUpdatedAt", "ServerUpdatedAt");
            bulk.ColumnMappings.Add("EffectiveActive", "EffectiveActive");
            bulk.ColumnMappings.Add("EditSequence", "EditSequence");
            bulk.ColumnMappings.Add("ParentCustomerID", "ParentCustomerID"); // temp 0; resolved in Sync

            await bulk.WriteToServerAsync(table, ct);

            await _audit.LogMessageAsync(runId, "CustomerBackup", "resp",
                message: $"Inserted {table.Rows.Count} rows", ct: ct);

            return table.Rows.Count;
        }



        public async Task<int> SyncCustomerDataAsync(Guid runId, bool fullRefresh = false, CancellationToken ct = default)
        {
            // Make sure backup has ParentCustomerID/Depth/paths before promoting
            await SyncCustomerBackupAsync(runId, ct);

            var affected = await _context.Database.ExecuteSqlRawAsync(
                "EXEC dbo.SyncCustomerFromBackup @FullRefresh = {0}",
                new object[] { fullRefresh ? 1 : 0 },
                cancellationToken: ct);

             await _audit.LogMessageAsync(runId, "Customer", "promote",
                message: $"SyncCustomerFromBackup done (FullRefresh={fullRefresh}); ExecuteSqlRaw affected={affected}", ct: ct);

            return affected;
        }

        public async Task<CustomerBackupSyncResult> SyncCustomerBackupAsync(
            Guid runId,
            CancellationToken ct = default)
        {
            // Resolve numeric parent FK, rebuild paths, recompute EffectiveActive — all in CustomerBackup.
            var sql = @"
-- 1) Resolve ParentCustomerID by ListID
UPDATE c
   SET c.ParentCustomerID = ISNULL(p.CustomerID, 0)
FROM dbo.CustomerBackup c
LEFT JOIN dbo.CustomerBackup p
  ON LTRIM(RTRIM(p.ID)) = LTRIM(RTRIM(c.ParentID));

-- 2) Rebuild hierarchy (paths/depth/root) within backup
IF OBJECT_ID('tempdb..#paths_bk') IS NOT NULL DROP TABLE #paths_bk;

;WITH roots AS (
  SELECT c.CustomerID, c.ParentCustomerID, c.CustomerName
  FROM dbo.CustomerBackup c
  LEFT JOIN dbo.CustomerBackup p ON p.CustomerID = c.ParentCustomerID
  WHERE c.CustomerID IS NOT NULL
    AND (c.ParentCustomerID = 0 OR p.CustomerID IS NULL)
),
tree AS (
  SELECT
      r.CustomerID,
      r.ParentCustomerID,
      r.CustomerName,
      CAST('/' + CAST(r.CustomerID AS NVARCHAR(32)) AS NVARCHAR(512))     AS PathIds,
      CAST(r.CustomerName AS NVARCHAR(1024))                               AS MaterializedPath,
      0                                                                    AS Depth,
      r.CustomerID                                                         AS RootId,
      CAST('/' + CAST(r.CustomerID AS NVARCHAR(32)) + '/' AS NVARCHAR(MAX)) AS visit
  FROM roots r
  UNION ALL
  SELECT
      c.CustomerID,
      c.ParentCustomerID,
      c.CustomerName,
      CAST(t.PathIds + '/' + CAST(c.CustomerID AS NVARCHAR(32)) AS NVARCHAR(512)),
      CAST(t.MaterializedPath + N'>' + c.CustomerName AS NVARCHAR(1024)),
      t.Depth + 1,
      t.RootId,
      CAST(t.visit + CAST(c.CustomerID AS NVARCHAR(32)) + '/' AS NVARCHAR(MAX))
  FROM dbo.CustomerBackup c
  JOIN tree t ON c.ParentCustomerID = t.CustomerID
  WHERE CHARINDEX('/' + CAST(c.CustomerID AS NVARCHAR(32)) + '/', t.visit) = 0
)
SELECT CustomerID, PathIds, MaterializedPath, Depth, RootId
INTO #paths_bk
FROM tree
OPTION (MAXRECURSION 32767);

UPDATE c
   SET c.PathIds          = p.PathIds,
       c.MaterializedPath = p.MaterializedPath,
       c.Depth            = p.Depth,
       c.RootId           = p.RootId
FROM dbo.CustomerBackup c
JOIN #paths_bk p ON p.CustomerID = c.CustomerID;

-- 3) Recompute EffectiveActive in backup: propagate IsActive down the tree
IF OBJECT_ID('tempdb..#eff_bk') IS NOT NULL DROP TABLE #eff_bk;

;WITH roots AS (
  SELECT c.CustomerID, c.ParentCustomerID, c.IsActive
  FROM dbo.CustomerBackup c
  LEFT JOIN dbo.CustomerBackup p ON p.CustomerID = c.ParentCustomerID
  WHERE (c.ParentCustomerID = 0 OR p.CustomerID IS NULL)
),
tree AS (
  SELECT r.CustomerID, r.ParentCustomerID,
         CAST(CASE WHEN r.IsActive = 1 THEN 1 ELSE 0 END AS BIT) AS EffectiveActive
  FROM roots r
  UNION ALL
  SELECT c.CustomerID, c.ParentCustomerID,
         CAST(CASE WHEN t.EffectiveActive = 1 AND c.IsActive = 1 THEN 1 ELSE 0 END AS BIT)
  FROM dbo.CustomerBackup c
  JOIN tree t ON c.ParentCustomerID = t.CustomerID
)
SELECT CustomerID, EffectiveActive
INTO #eff_bk
FROM tree
OPTION (MAXRECURSION 32767);

UPDATE c
   SET c.EffectiveActive = e.EffectiveActive
FROM dbo.CustomerBackup c
JOIN #eff_bk e ON e.CustomerID = c.CustomerID;
";
            var conn = (SqlConnection)_context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            using var tx = await _context.Database.BeginTransactionAsync(ct);
            var affected = await _context.Database.ExecuteSqlRawAsync(sql, ct);
            await tx.CommitAsync(ct);

            await _audit.LogMessageAsync(runId, "CustomerBackup", "promote",
                message: "Hierarchy and EffectiveActive recomputed; parents resolved.", ct: ct);

            // For backup pilot we only insert; set updated/deactivated to 0
            return new CustomerBackupSyncResult
            {
                Inserted = 0,
                Updated = 0,
                Deactivated = 0
            };
        }

        // ---------------- helpers ----------------
        private static DataTable BuildTable(Guid runId, List<CustomerData> rows)
        {
            var dt = new DataTable("CustomerBackup");

            // Columns matching dbo.CustomerBackup (excluding identity + rowversion)
            dt.Columns.Add(new DataColumn("ID", typeof(string)) { MaxLength = 50 });
            dt.Columns.Add(new DataColumn("ParentID", typeof(string)) { MaxLength = 50 });
            dt.Columns.Add(new DataColumn("CustomerName", typeof(string)) { MaxLength = 255 });
            dt.Columns.Add(new DataColumn("ParentName", typeof(string)) { MaxLength = 255 });
            dt.Columns.Add(new DataColumn("Company", typeof(string)) { MaxLength = 255 });
            dt.Columns.Add(new DataColumn("FullName", typeof(string)) { MaxLength = 255 });
            dt.Columns.Add(new DataColumn("FirstName", typeof(string)) { MaxLength = 100 });
            dt.Columns.Add(new DataColumn("LastName", typeof(string)) { MaxLength = 100 });
            dt.Columns.Add(new DataColumn("AccountNumber", typeof(string)) { MaxLength = 50 });
            dt.Columns.Add(new DataColumn("Phone", typeof(string)) { MaxLength = 50 });
            dt.Columns.Add(new DataColumn("Email", typeof(string)) { MaxLength = 255 });
            dt.Columns.Add(new DataColumn("Notes", typeof(string)) { MaxLength = int.MaxValue });
            dt.Columns.Add(new DataColumn("JobStatus", typeof(string)) { MaxLength = 100 });
            dt.Columns.Add(new DataColumn("JobStartDate", typeof(DateTime)) { AllowDBNull = true });
            dt.Columns.Add(new DataColumn("JobProjectedEndDate", typeof(DateTime)) { AllowDBNull = true });
            dt.Columns.Add(new DataColumn("JobDescription", typeof(string)) { MaxLength = int.MaxValue });
            dt.Columns.Add(new DataColumn("JobType", typeof(string)) { MaxLength = 100 });
            dt.Columns.Add(new DataColumn("JobTypeId", typeof(string)) { MaxLength = 50 });
            dt.Columns.Add(new DataColumn("Description", typeof(string)) { MaxLength = int.MaxValue });
            dt.Columns.Add(new DataColumn("SubLevelId", typeof(int)));
            dt.Columns.Add(new DataColumn("FullAddress", typeof(string)) { MaxLength = int.MaxValue });
            dt.Columns.Add(new DataColumn("LastUpdated", typeof(DateTime)));
            dt.Columns.Add(new DataColumn("UpdateType", typeof(string)) { MaxLength = 1 });
            dt.Columns.Add(new DataColumn("MaterializedPath", typeof(string)) { MaxLength = 1024 });
            dt.Columns.Add(new DataColumn("PathIds", typeof(string)) { MaxLength = 512 });
            dt.Columns.Add(new DataColumn("Depth", typeof(int)));
            dt.Columns.Add(new DataColumn("RootId", typeof(int)));
            dt.Columns.Add(new DataColumn("QbLastUpdated", typeof(DateTime)) { AllowDBNull = true });
            dt.Columns.Add(new DataColumn("IsActive", typeof(bool)));
            dt.Columns.Add(new DataColumn("ServerUpdatedAt", typeof(DateTime)));
            dt.Columns.Add(new DataColumn("EffectiveActive", typeof(bool)));
            dt.Columns.Add(new DataColumn("EditSequence", typeof(string)) { MaxLength = 50 });
            dt.Columns.Add(new DataColumn("ParentCustomerID", typeof(int)));

            var now = DateTime.UtcNow;

            foreach (var x in rows)
            {
                var r = dt.NewRow();
                r["ID"] = Trunc(x.ID, 50) ?? "";
                r["ParentID"] = Trunc(x.ParentID, 50) ?? "";
                r["CustomerName"] = Trunc(x.Name, 255) ?? "";
                r["ParentName"] = Trunc(x.ParentName, 255) ?? "";
                r["Company"] = Trunc(x.Company, 255) ?? "";
                r["FullName"] = Trunc(x.FullName, 255) ?? "";
                r["FirstName"] = Trunc(x.FirstName, 100) ?? "";
                r["LastName"] = Trunc(x.LastName, 100) ?? "";
                r["AccountNumber"] = Trunc(x.AccountNumber, 50) ?? "";
                r["Phone"] = Trunc(x.Phone, 50) ?? "";
                r["Email"] = Trunc(x.Email, 255) ?? "";
                r["Notes"] = x.Notes ?? "";
                r["JobStatus"] = Trunc(x.JobStatus, 100) ?? "";
                r["JobStartDate"] = TryDate(x.JobStartDate) ?? (object)DBNull.Value;
                r["JobProjectedEndDate"] = TryDate(x.JobProjectedEndDate) ?? (object)DBNull.Value;
                r["JobDescription"] = x.JobDescription ?? "";
                r["JobType"] = Trunc(x.JobType, 100) ?? "";
                r["JobTypeId"] = Trunc(x.JobTypeId, 50) ?? "";
                r["Description"] = x.Description ?? "";
                r["SubLevelId"] = x.SubLevelId;
                r["FullAddress"] = x.FullAddress ?? "";
                r["LastUpdated"] = x.LastUpdated == default ? now : x.LastUpdated;
                r["UpdateType"] = string.IsNullOrEmpty(x.UpdateType) ? "A" : x.UpdateType.Substring(0, 1);
                r["MaterializedPath"] = x.MaterializedPath ?? "";
                r["PathIds"] = x.PathIds ?? "";
                r["Depth"] = x.Depth;
                r["RootId"] = x.RootId;
                r["QbLastUpdated"] = (object?)x.QbLastUpdated ?? DBNull.Value;
                r["IsActive"] = x.IsActive;
                r["ServerUpdatedAt"] = x.ServerUpdatedAt == default ? now : x.ServerUpdatedAt;
                r["EffectiveActive"] = x.EffectiveActive;
                r["EditSequence"] = Trunc(x.EditSequence, 50) ?? "";
                r["ParentCustomerID"] = x.ParentCustomerID; // 0 for now; resolved in Sync

                dt.Rows.Add(r);
            }

            return dt;

            static string? Trunc(string? s, int len) =>
                string.IsNullOrEmpty(s) ? s : (s.Length <= len ? s : s.Substring(0, len));

            static DateTime? TryDate(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return null;
                return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var dto)
                    ? dto.UtcDateTime : (DateTime?)null;
            }
        }
    }
}
