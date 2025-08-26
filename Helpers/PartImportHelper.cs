using System;
using System.Collections.Generic;
using System.Globalization;
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
    public static class PartImportHelper
    {
        private const int SAMPLE_LIMIT = 10;

        private static readonly string[] ExpectedHeaders =
            { "Item", "Description", "Preferred Vendor", "U/M", "Price" };

        public static bool ValidateHeaders(IEnumerable<string> headers)
        {
            var headerArray = headers.ToArray();
            if (headerArray.Length < ExpectedHeaders.Length) return false;

            for (int i = 0; i < ExpectedHeaders.Length; i++)
            {
                if (!string.Equals(headerArray[i]?.Trim(), ExpectedHeaders[i], StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning("Header mismatch at position {Pos}: expected {Expected}, got {Got}",
                        i, ExpectedHeaders[i], headerArray[i]);
                    return false;
                }
            }
            return true;
        }

        public static IEnumerable<PartCSVRow> ParsePartRows(Stream xlsxStream)
        {
            using var workbook = new XLWorkbook(xlsxStream);
            var ws = workbook.Worksheet("Sheet1");

            // Header
            var headers = ws.Row(1).Cells().Select(c => c.GetString()).ToList();
            if (!ValidateHeaders(headers))
                throw new InvalidDataException("Invalid header row in PartUpload.xlsx");

            // Data rows start at row 2
            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var item = row.Cell(1).GetString();
                var desc = row.Cell(2).GetString();
                var uom = row.Cell(4).GetString();
                var priceStr = row.Cell(5).GetString();

                // Strip currency symbols/commas; parse tolerant
                var cleaned = (priceStr ?? string.Empty)
                    .Replace("$", "").Replace(",", "").Trim();
                decimal price = 0m;
                _ = decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out price);

                yield return new PartCSVRow
                {
                    Item = item,
                    Description = desc,
                    Uom = uom,
                    Price = price
                };
            }
        }

        public static PartCSVRow NormalizeRow(PartCSVRow row)
        {
            row.Item = (row.Item ?? "").Trim();
            row.Description = (row.Description ?? "").Trim();
            row.Uom = (row.Uom ?? "").Trim();
            return row;
        }

        /// <summary>
        /// Upserts rows into InventoryDataNew and returns counts + proof samples for
        /// Inserted / Updated / Reactivated (MarkedInactive is handled separately).
        /// </summary>
        public static async Task<PartImportResult> ApplyUpsertsAsync(
            RaymarInventoryDBContext context,
            IEnumerable<PartCSVRow> rows,
            CancellationToken ct = default)
        {
            int inserted = 0, updated = 0, reactivated = 0, rejected = 0;

            var result = new PartImportResult(); // carries counts + samples
            var normalized = rows.Select(NormalizeRow).ToList();

            // ----- Build a CSV map (last row wins), skip blank keys -----
            var csvMap = new Dictionary<string, PartCSVRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in normalized)
            {
                var key = (r.Item ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key)) { rejected++; continue; }
                csvMap[key] = r; // last one wins
            }

            // ----- Build DB key→entity map, skip blank keys, resolve duplicates deterministically -----
            var existingList = await context.InventoryDataNews
                .AsTracking()
                .Where(x => !string.IsNullOrWhiteSpace(x.ManufacturerPartNumber))
                .ToListAsync(ct);

            var existingParts = new Dictionary<string, InventoryDataNew>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in existingList)
            {
                var key = p.ManufacturerPartNumber!.Trim();
                if (!existingParts.TryAdd(key, p))
                {
                    var cur = existingParts[key];
                    bool candidateWins =
                        (cur.IsActive != true && p.IsActive == true) ||
                        ((cur.IsActive == p.IsActive) && /* prefer the most recent row */
                         (p.InventoryId > cur.InventoryId));
                    if (candidateWins) existingParts[key] = p;
                }
            }

            // ----- Upsert loop -----
            foreach (var (key, row) in csvMap)
            {
                if (existingParts.TryGetValue(key, out var entity))
                {
                    // BEFORE snapshot
                    var before = new PartChangeSample
                    {
                        Key = key,
                        BeforeItemName = entity.ItemName,
                        BeforePrice = entity.SalesPrice,
                        BeforeIsActive = entity.IsActive
                    };

                    bool wasInactive = entity.IsActive != true;

                    // UPDATE
                    entity.ItemName = row.Description;
                    entity.Description = row.Description;
                    entity.SalesPrice = row.Price;
                    entity.IsActive = true;

                    // AFTER snapshot
                    var after = new PartChangeSample
                    {
                        Key = key,
                        AfterItemName = entity.ItemName,
                        AfterPrice = entity.SalesPrice,
                        AfterIsActive = entity.IsActive,
                        Action = wasInactive ? "Reactivated" : "Updated",
                        BeforeItemName = before.BeforeItemName,
                        BeforePrice = before.BeforePrice,
                        BeforeIsActive = before.BeforeIsActive
                    };

                    if (wasInactive)
                    {
                        reactivated++;
                        TryAddSample(result.ReactivatedSamples, after);
                    }
                    else
                    {
                        updated++;
                        TryAddSample(result.UpdatedSamples, after);
                    }
                }
                else
                {
                    // INSERT
                    var newEntity = new InventoryDataNew
                    {
                        ManufacturerPartNumber = key,
                        ItemName = row.Description,
                        Description = row.Description,
                        SalesPrice = row.Price,
                        IsActive = true
                    };
                    await context.InventoryDataNews.AddAsync(newEntity, ct);
                    inserted++;

                    var sample = new PartChangeSample
                    {
                        Key = key,
                        Action = "Inserted",
                        // Before = nulls
                        AfterItemName = row.Description,
                        AfterPrice = row.Price,
                        AfterIsActive = true
                    };
                    TryAddSample(result.InsertedSamples, sample);
                }
            }

            await context.SaveChangesAsync(ct);

            result.Inserted = inserted;
            result.Updated = updated;
            result.Reactivated = reactivated;
            result.Rejected = rejected;
            result.Timestamp = DateTime.UtcNow;
            return result;
        }


        /// <summary>
        /// Marks InventoryDataNew rows inactive if their key is not in the activePartNumbers set.
        /// Returns (count, samples).
        /// </summary>
        public static async Task<(int MarkedInactive, List<PartChangeSample> Samples)> MarkInactiveAsync(
            RaymarInventoryDBContext context,
            IEnumerable<string> activePartNumbers,
            CancellationToken ct = default)
        {
            var samples = new List<PartChangeSample>(SAMPLE_LIMIT);

            // Normalize & de-dup the active keys from the CSV
            var activeSet = new HashSet<string>(
                activePartNumbers
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);

            // Only deactivate rows that:
            //  - are currently active
            //  - have a non-blank ManufacturerPartNumber
            //  - are NOT present in the current CSV's active set
            var toDeactivate = context.InventoryDataNews
                .Where(p => p.IsActive == true
                         && !string.IsNullOrWhiteSpace(p.ManufacturerPartNumber)
                         && !activeSet.Contains(p.ManufacturerPartNumber!));

            int count = 0;
            await foreach (var part in toDeactivate.AsAsyncEnumerable().WithCancellation(ct))
            {
                // BEFORE
                var before = new PartChangeSample
                {
                    Key = part.ManufacturerPartNumber ?? string.Empty,
                    BeforeItemName = part.ItemName,
                    BeforePrice = part.SalesPrice,
                    BeforeIsActive = part.IsActive
                };

                part.IsActive = false;
                count++;

                // AFTER
                var after = new PartChangeSample
                {
                    Key = before.Key,
                    Action = "MarkedInactive",
                    BeforeItemName = before.BeforeItemName,
                    BeforePrice = before.BeforePrice,
                    BeforeIsActive = before.BeforeIsActive,
                    AfterItemName = part.ItemName,
                    AfterPrice = part.SalesPrice,
                    AfterIsActive = part.IsActive
                };

                TryAddSample(samples, after);
            }

            await context.SaveChangesAsync(ct);
            return (count, samples);
        }


        /// <summary>
        /// Merge the upsert result with the inactive counts/samples into the final result.
        /// </summary>
        public static PartImportResult BuildAuditResult(
            PartImportResult upserts,
            int markedInactive,
            List<PartChangeSample> markedInactiveSamples)
        {
            return new PartImportResult
            {
                Inserted = upserts.Inserted,
                Updated = upserts.Updated,
                Reactivated = upserts.Reactivated,
                MarkedInactive = markedInactive,
                Rejected = upserts.Rejected,
                Timestamp = upserts.Timestamp,

                InsertedSamples = upserts.InsertedSamples,
                UpdatedSamples = upserts.UpdatedSamples,
                ReactivatedSamples = upserts.ReactivatedSamples,
                MarkedInactiveSamples = markedInactiveSamples ?? new List<PartChangeSample>()
            };
        }

        private static void TryAddSample(List<PartChangeSample> list, PartChangeSample sample)
        {
            if (list.Count < SAMPLE_LIMIT) list.Add(sample);
        }
    }
}
