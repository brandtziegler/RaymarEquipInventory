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

namespace RaymarEquipmentInventory.Services
{
    public class TechWOService : ITechWOService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public TechWOService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }

        public async Task<bool> InsertTechWOAsync(DTOs.TechnicianWorkOrder techWODto)
        {
            try
            {
                // Step 1: Basic validation
                if (techWODto.SheetID <= 0 || techWODto.TechnicianID <=0)
                {
                    Log.Warning("SheetId and TechnicianID are required.");
                    return false;
                }

                // Step 2: Create entity
                var newEntry = new Models.TechnicianWorkOrder
                {
                    SheetId = techWODto.SheetID,
                    TechnicianId = techWODto.TechnicianID,
       
                };

                // Step 3: Save to DB
                await _context.TechnicianWorkOrders.AddAsync(newEntry);
                await _context.SaveChangesAsync();

                Log.Information($"✅ Inserted Technician WorkOrder for SheetID {techWODto.SheetID} and TechnicianID {techWODto.TechnicianID}");
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? "No inner exception";
                Log.Error($"❌ Failed to insert BillingInfo: {ex.Message} | Inner: {inner}");
                return false;
            }
        }




    }

}

