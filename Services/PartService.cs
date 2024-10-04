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
    public class PartService : IPartService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public PartService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }


        public async Task<DTOs.PartsUsed> GetPartByID(int partID)
        {
            // First, let's get the tech, and include the related Person and TechLicences
            var partUsed = await _context.PartsUseds
                .Include(t => t.Inventory)  // Include the related person
                .FirstOrDefaultAsync(t => t.PartUsedId == partID);

            if (partUsed == null)
            {
                return null; // Or throw an exception if that's your style
            }

            // We map it to the DTO you showed in the image
            var partUsedDTO = new DTOs.PartsUsed
            {
                PartUsedId = partUsed.PartUsedId,
                InventoryId = partUsed.InventoryId ?? 0,
                QtyUsed = partUsed.QtyUsed,
                SheetId = partUsed.SheetId,
                Notes = partUsed.Notes,
                InventoryData = new DTOs.InventoryData
                {
                    QuickBooksInvId = partUsed.Inventory.QuickBooksInvId,
                    ItemName = partUsed.Inventory.ItemName,
                    ManufacturerPartNumber = partUsed.Inventory.ManufacturerPartNumber,
                    Description = partUsed.Inventory.Description,
                    Cost = partUsed.Inventory.Cost,
                    SalesPrice = partUsed.Inventory.SalesPrice,
                    ReorderPoint = partUsed.Inventory.ReorderPoint
                }
            };

            return partUsedDTO;
        }

        public async Task<List<DTOs.PartsUsed>> GetPartsByWorkOrder(int workOrderID)
        {


            var partsUsedList = await _context.PartsUseds
                .Include(t => t.Inventory)  // Include the related person
                .Where(t => t.SheetId == workOrderID)
                .ToListAsync();

            // Map the list of technicians to the DTO
            var partsUsedDTO = partsUsedList.Select(partUsed => new DTOs.PartsUsed
            {
                PartUsedId = partUsed.PartUsedId,
                InventoryId = partUsed.InventoryId ?? 0,
                QtyUsed = partUsed.QtyUsed,
                SheetId = partUsed.SheetId,
                Notes = partUsed.Notes,
                InventoryData = new DTOs.InventoryData
                {
                    QuickBooksInvId = partUsed.Inventory.QuickBooksInvId,
                    ItemName = partUsed.Inventory.ItemName,
                    ManufacturerPartNumber = partUsed.Inventory.ManufacturerPartNumber,
                    Description = partUsed.Inventory.Description,
                    Cost = partUsed.Inventory.Cost,
                    SalesPrice = partUsed.Inventory.SalesPrice,
                    ReorderPoint = partUsed.Inventory.ReorderPoint
                }
            }).ToList();

            return partsUsedDTO;
        }


    }

}

