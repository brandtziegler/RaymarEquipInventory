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



        public async Task<bool> InsertWorkOrderFee(DTOs.WorkOrderFee workOrdFee)
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

                if (workOrdFee.Qty <= 0)
                {
                    Log.Warning("Qty must be greater than 0.");
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

