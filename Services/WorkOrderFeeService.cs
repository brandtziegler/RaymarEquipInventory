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
using Microsoft.AspNetCore.Server.IISIntegration;

namespace RaymarEquipmentInventory.Services
{
    public class WorkOrderFeeService : IWorkOrderFeeService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public WorkOrderFeeService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }


        public async Task<bool> DeleteWorkOrderFees(int technicianWorkOrderId, CancellationToken cancellationToken = default)
        {
            try
            {
                if (technicianWorkOrderId <= 0)
                {
                    Log.Warning("TechnicianWorkOrderID is required for deletion.");
                    return false;
                }

                var feesToDelete = await _context.WorkOrderFees
                    .Where(f => f.TechnicianWorkOrderId == technicianWorkOrderId)
                    .ToListAsync();

                if (!feesToDelete.Any())
                {
                    Log.Information($"🟡 No WorkOrderFees found for TechnicianWorkOrderID {technicianWorkOrderId}. Nothing to delete.");
                    return true;
                }

                _context.WorkOrderFees.RemoveRange(feesToDelete);
                await _context.SaveChangesAsync();

                Log.Information($"🗑️ Deleted {feesToDelete.Count} WorkOrderFee(s) for TechnicianWorkOrderID {technicianWorkOrderId}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"❌ Failed to delete WorkOrderFees for TechnicianWorkOrderID {technicianWorkOrderId}: {ex.Message}");
                return false;
            }
        }

        public async Task<int> ReplaceFeeVisibilityAsync(
    int technicianWorkOrderId,
    IEnumerable<DTOs.FeeVisibilityDto> items,
    CancellationToken cancellationToken = default)
        {
            if (technicianWorkOrderId <= 0) return 0;

            var src = (items ?? Enumerable.Empty<DTOs.FeeVisibilityDto>()).ToList();
            foreach (var v in src)
                if (v.TechnicianWorkOrderID <= 0) v.TechnicianWorkOrderID = technicianWorkOrderId;

            // delete current visibility rows for this TechWO
            var existing = await _context.FeeVisibilities
                .Where(x => x.TechnicianWorkOrderId == technicianWorkOrderId)
                .ToListAsync(cancellationToken);

            if (existing.Count > 0)
            {
                _context.FeeVisibilities.RemoveRange(existing);
                await _context.SaveChangesAsync(cancellationToken);
            }

            // empty list => no rows → defaults to visible via COALESCE on read
            if (src.Count == 0) return 0;

            // distinct per LabourType
            var distinct = src.GroupBy(v => v.LabourTypeID).Select(g => g.First()).ToList();

            var entities = distinct.Select(v => new Models.FeeVisibility
            {
                TechnicianWorkOrderId = technicianWorkOrderId,
                LabourTypeId = v.LabourTypeID,
                IsVisible = v.IsVisible != 0,           // EF model likely bool (BIT)
                UpdatedAtUtc = DateTime.UtcNow
            }).ToList();

            _context.FeeVisibilities.AddRange(entities);
            await _context.SaveChangesAsync(cancellationToken);
            return entities.Count;
        }

        public async Task<int> InsertWorkOrderFeeBulkAsync(
    IEnumerable<DTOs.WorkOrderFee> items,
    CancellationToken cancellationToken = default)
        {
            var src = items?.ToList() ?? new List<DTOs.WorkOrderFee>();
            if (src.Count == 0) return 0;

            var entities = new List<Models.WorkOrderFee>(src.Count);

            foreach (var fee in src)
            {
                // Basic validation (keep zeros if that’s your pattern; just clamp negatives)
                if (fee.TechnicianWorkOrderID <= 0 || fee.FlatLabourID <= 0 || fee.LabourTypeID <= 0)
                    continue;

                if (fee.Qty < 0) fee.Qty = 0;

                entities.Add(new Models.WorkOrderFee
                {
                    TechnicianWorkOrderId = fee.TechnicianWorkOrderID,
                    FlatLabourId = fee.FlatLabourID,
                    LabourTypeId = fee.LabourTypeID,
                    Qty = fee.Qty,
                    WorkDescription = fee.WorkDescription?.Trim() ?? string.Empty
                });
            }

            if (entities.Count == 0) return 0;

            var prev = _context.ChangeTracker.AutoDetectChangesEnabled;
            try
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = false; // same optimization as labour
                _context.WorkOrderFees.AddRange(entities);
                await _context.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = prev;
            }

            Log.Information("✅ Bulk inserted {Count} WorkOrderFee rows.", entities.Count);
            return entities.Count;
        }



        public async Task<bool> InsertWorkOrderFee(DTOs.WorkOrderFee workOrdFee, CancellationToken cancellationToken = default)
        {
            try
            {
                // Step 1: Basic validation
                if (workOrdFee.TechnicianWorkOrderID <= 0)
                {
                    Log.Warning("TechnicianWorkOrderID is required.");
                    return false;
                }

                if (workOrdFee.FlatLabourID <= 0)
                {
                    Log.Warning("FlatLabourID is required.");
                    return false;
                }

                if (workOrdFee.LabourTypeID <= 0)
                {
                    Log.Warning("LabourTypeID is required.");
                    return false;
                }



                // Step 2: Create new EF entity
                var newEntry = new Models.WorkOrderFee
                {
                    TechnicianWorkOrderId = workOrdFee.TechnicianWorkOrderID,
                    FlatLabourId = workOrdFee.FlatLabourID,
                    LabourTypeId = workOrdFee.LabourTypeID,
                    Qty = workOrdFee.Qty,
                    WorkDescription = workOrdFee.WorkDescription?.Trim() ?? ""
                };

                // Step 3: Save to DB
                await _context.WorkOrderFees.AddAsync(newEntry);
                await _context.SaveChangesAsync();

                Log.Information($"✅ Inserted Work Order Fee → TechWorkOrderID {workOrdFee.TechnicianWorkOrderID}, FlatLabourID {workOrdFee.FlatLabourID}, Qty {workOrdFee.Qty}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"❌ Failed to insert Work Order Fee: {ex.Message}");
                return false;
            }
        }






    }

}

