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

        public async Task<int> BulkInsertInventoryAsync(Guid runId, IEnumerable<InventoryItemDto> items, CancellationToken ct = default)
        {
            // materialize once
            var list = items is List<InventoryItemDto> l ? l : new List<InventoryItemDto>(items);
            if (list.Count == 0)
            {
                await _audit.LogMessageAsync(runId, "InventoryStaging", "resp", message: "0 rows (empty batch)", ct: ct);
                return 0;
            }

            var table = BuildTable(runId, list);

            var conn = (SqlConnection)_context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            using var bulk = new SqlBulkCopy(conn)
            {
                DestinationTableName = "dbo.InventoryStaging",
                BulkCopyTimeout = 0,   // no timeout
                BatchSize = Math.Min(1000, table.Rows.Count)
            };

            bulk.ColumnMappings.Add("RunId", "RunId");
            bulk.ColumnMappings.Add("ListID", "ListID");
            bulk.ColumnMappings.Add("FullName", "FullName");
            bulk.ColumnMappings.Add("EditSequence", "EditSequence");
            bulk.ColumnMappings.Add("QuantityOnHand", "QuantityOnHand");
            bulk.ColumnMappings.Add("SalesPrice", "SalesPrice");
            bulk.ColumnMappings.Add("PurchaseCost", "PurchaseCost");

            await bulk.WriteToServerAsync(table, ct);

            await _audit.LogMessageAsync(runId, "InventoryStaging", "resp",
                message: $"Inserted {table.Rows.Count} rows", ct: ct);

            return table.Rows.Count;
        }

        private static DataTable BuildTable(Guid runId, List<InventoryItemDto> items)
        {
            var dt = new DataTable();
            dt.Columns.Add("RunId", typeof(Guid));
            dt.Columns.Add("ListID", typeof(string));
            dt.Columns.Add("FullName", typeof(string));
            dt.Columns.Add("EditSequence", typeof(string));
            dt.Columns.Add("QuantityOnHand", typeof(decimal));
            dt.Columns.Add("SalesPrice", typeof(decimal));
            dt.Columns.Add("PurchaseCost", typeof(decimal));

            foreach (var x in items)
            {
                var row = dt.NewRow();
                row["RunId"] = runId;
                row["ListID"] = (object?)x.ListID ?? DBNull.Value;
                row["FullName"] = (object?)x.FullName ?? DBNull.Value;
                row["EditSequence"] = (object?)x.EditSequence ?? DBNull.Value;
                row["QuantityOnHand"] = (object?)x.QuantityOnHand ?? 0m; // staging column is NOT NULL
                row["SalesPrice"] = (object?)x.SalesPrice ?? DBNull.Value;
                row["PurchaseCost"] = (object?)x.PurchaseCost ?? DBNull.Value;
                dt.Rows.Add(row);
            }
            return dt;
        }
    }
}

