using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Models;
using Serilog;

namespace RaymarEquipmentInventory.Helpers
{
    public static class CustomerImportHelper
    {
        private static readonly string[] ExpectedHeaders =
        {
            "Full Name", "Name", "Company Name", "Bill To Address", "Main Phone", "Main Email", "Is Active"
        };

        public static bool ValidateHeaders(IReadOnlyList<string> headers)
        {
            if (headers.Count < ExpectedHeaders.Length) return false;
            for (int i = 0; i < ExpectedHeaders.Length; i++)
            {
                if (!string.Equals(headers[i]?.Trim(), ExpectedHeaders[i], StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning("Customer header mismatch at {Pos}: expected {Expected}, got {Got}",
                        i, ExpectedHeaders[i], headers[i]);
                    return false;
                }
            }
            return true;
        }

        public static IEnumerable<CustomerCsvRow> ParseCustomerRows(Stream xlsxStream)
        {
            using var workbook = new XLWorkbook(xlsxStream);
            var ws = workbook.Worksheet("Sheet1");

            var headers = ws.Row(1).Cells().Select(c => c.GetString()).ToList();
            if (!ValidateHeaders(headers))
                throw new InvalidDataException("Invalid header row in CustomerUpsert.xlsx");

            foreach (var row in ws.RowsUsed().Skip(1))
            {
                string fullName = row.Cell(1).GetString() ?? "";
                string name = row.Cell(2).GetString() ?? "";
                string company = row.Cell(3).GetString() ?? "";
                string addr = row.Cell(4).GetString() ?? "";
                string phone = row.Cell(5).GetString() ?? "";
                string email = row.Cell(6).GetString() ?? "";
                string active = row.Cell(7).GetString() ?? "";

                // normalize boolean
                bool isActive = active.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)
                                || active.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
                                || active.Trim().Equals("1", StringComparison.OrdinalIgnoreCase)
                                || active.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);

                // derive parent + depth from FullName (Customer:Job:Subjob)
                string? parentFullName = null;
                int depth = 0;
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    depth = fullName.Count(ch => ch == ':');
                    int pos = fullName.LastIndexOf(':');
                    if (pos > 0) parentFullName = fullName.Substring(0, pos);
                }

                yield return new CustomerCsvRow
                {
                    FullName = (fullName ?? "").Trim(),
                    Name = (name ?? "").Trim(),
                    Company = (company ?? "").Trim(),
                    FullAddress = Flatten(addr),
                    Phone = (phone ?? "").Trim(),
                    Email = (email ?? "").Trim(),
                    IsActive = isActive,
                    ParentFullName = string.IsNullOrWhiteSpace(parentFullName) ? null : parentFullName.Trim(),
                    Depth = depth
                };
            }
        }

        private static string Flatten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", ", ").Replace('\n', ' ').Replace('\r', ' ').Replace("  ", " ").Trim();
        }

        /// <summary>
        /// Upsert customers by FullName. Process in depth layers so parents exist before children.
        /// Sets ParentCustomerID, basic fields, and returns counts.
        /// </summary>
        public static async Task<CustomerImportResult> ApplyUpsertsAsync(
            RaymarInventoryDBContext context,
            IEnumerable<CustomerCsvRow> rows,
            CancellationToken ct = default)
        {
            var result = new CustomerImportResult();
            var normalized = rows
                .Select(Norm)
                .Where(r => !string.IsNullOrWhiteSpace(r.FullName) && !string.IsNullOrWhiteSpace(r.Name))
                .ToList();

            // Last-row-wins per FullName
            var csvMap = new Dictionary<string, CustomerCsvRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in normalized)
                csvMap[r.FullName] = r;

            // preload existing by FullName
            var existing = await context.Customers  // DbSet<Customer>
                .AsTracking()
                .ToListAsync(ct);

            var byFullName = new Dictionary<string, Customer>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in existing)
            {
                var key = (e.FullName ?? "").Trim();
                if (!string.IsNullOrEmpty(key))
                    byFullName[key] = e;
            }

            // Also map FullName -> CustomerID for quick parent resolution as we go
            var idByFullName = byFullName.ToDictionary(k => k.Key, v => v.Value.CustomerId, StringComparer.OrdinalIgnoreCase);

            // process by depth (parents first)
            var groups = csvMap.Values.GroupBy(r => r.Depth).OrderBy(g => g.Key);
            foreach (var group in groups)
            {
                foreach (var row in group)
                {
                    // resolve parent id (if any)
                    int? parentId = null;
                    if (!string.IsNullOrWhiteSpace(row.ParentFullName) &&
                        idByFullName.TryGetValue(row.ParentFullName!, out var pid))
                    {
                        parentId = pid;
                    }

                    if (byFullName.TryGetValue(row.FullName, out var entity))
                    {
                        bool changed = false;
                        bool reparented = false;

                        // detect and apply changes
                        if (!string.Equals(entity.CustomerName, row.Name, StringComparison.Ordinal))
                        { entity.CustomerName = row.Name; changed = true; }

                        if (!string.Equals(entity.Company, row.Company, StringComparison.Ordinal))
                        { entity.Company = row.Company; changed = true; }

                        if (!string.Equals(entity.FullAddress ?? "", row.FullAddress, StringComparison.Ordinal))
                        { entity.FullAddress = row.FullAddress; changed = true; }

                        if (!string.Equals(entity.Phone ?? "", row.Phone, StringComparison.Ordinal))
                        { entity.Phone = row.Phone; changed = true; }

                        if (!string.Equals(entity.Email ?? "", row.Email, StringComparison.Ordinal))
                        { entity.Email = row.Email; changed = true; }

                        if ((entity.IsActive ?? false) != row.IsActive)
                        { entity.IsActive = row.IsActive; changed = true; }

                        // keep SubLevelId congruent (optional but nice)
                        if (entity.SubLevelId != row.Depth)
                        { entity.SubLevelId = row.Depth; changed = true; }

                        // reparent if needed
                        if (entity.ParentCustomerId != parentId)
                        {
                            entity.ParentCustomerId = parentId;
                            changed = true;
                            reparented = true;
                        }

                        if (changed)
                        {
                            entity.ServerUpdatedAt = DateTime.UtcNow;
                            entity.UpdateType = "U";
                            result.Updated++;
                            if (reparented) result.Reparented++;
                        }
                    }
                    else
                    {
                        // INSERT
                        var e = new Customer
                        {
                            CustomerName = row.Name,
                            FullName = row.FullName,
                            Company = row.Company,
                            FullAddress = row.FullAddress,
                            Phone = row.Phone,
                            Email = row.Email,
                            IsActive = row.IsActive,
                            SubLevelId = row.Depth,
                            ParentCustomerId = parentId,
                            ServerUpdatedAt = DateTime.UtcNow,
                            UpdateType = "A"
                        };
                        await context.Customers.AddAsync(e, ct);
                        result.Inserted++;

                        // make it visible to later children in this batch after SaveChanges
                    }
                }

                // commit this depth, so inserts get their identity values
                await context.SaveChangesAsync(ct);

                // extend the FullName -> id map with any inserts from this depth
                foreach (var e in context.ChangeTracker.Entries<Customer>()
                                         .Where(x => x.State == EntityState.Unchanged)) // just saved
                {
                    var key = (e.Entity.FullName ?? "").Trim();
                    if (!string.IsNullOrEmpty(key))
                        idByFullName[key] = e.Entity.CustomerId;
                    byFullName[key] = e.Entity;
                }
            }

            return result;
        }

        private static CustomerCsvRow Norm(CustomerCsvRow r)
        {
            static string fix(string? s) =>
                (s ?? "").Replace(((char)160).ToString(), " ").Trim();

            r.FullName = fix(r.FullName);
            r.Name = fix(r.Name);
            r.Company = fix(r.Company);
            r.FullAddress = fix(r.FullAddress);
            r.Phone = fix(r.Phone);
            r.Email = fix(r.Email);

            // recompute derived
            r.Depth = r.FullName.Count(ch => ch == ':');
            int pos = r.FullName.LastIndexOf(':');
            r.ParentFullName = (pos > 0) ? r.FullName.Substring(0, pos).Trim() : null;

            return r;
        }
    }
}
