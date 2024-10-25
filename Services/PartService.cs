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

        public async Task<bool> UpdatePart(DTOs.PartsUsed partsUsedDto)
        {
            // Start by checking for required fields in the DTO
            if (partsUsedDto.PartUsedId == 0 || partsUsedDto.SheetId == null || partsUsedDto.InventoryId == 0 || partsUsedDto.QtyUsed == null)
            {
                Log.Warning("Failed to update part: PartUsedId, SheetID, InventoryId, and QtyUsed are required fields.");
                return false;
            }

            try
            {
                // Step 1: Retrieve the existing PartsUsed entity from the database
                var partUsedEntity = await _context.PartsUseds
                    .FirstOrDefaultAsync(p => p.PartUsedId == partsUsedDto.PartUsedId);

                if (partUsedEntity == null)
                {
                    Log.Warning($"Part with PartUsedId {partsUsedDto.PartUsedId} not found.");
                    return false;
                }

                // Step 2: Update the entity's properties with values from the DTO
                partUsedEntity.InventoryId = partsUsedDto.InventoryId;
                partUsedEntity.QtyUsed = partsUsedDto.QtyUsed.Value;
                partUsedEntity.Notes = partsUsedDto.Notes ?? string.Empty;  // Optional, use empty string if null

                // Step 3: Save changes to the database
                _context.PartsUseds.Update(partUsedEntity);
                await _context.SaveChangesAsync();

                Log.Information($"Successfully updated part with PartUsedId {partsUsedDto.PartUsedId} for Work Order {partsUsedDto.SheetId}");
                return true;
            }
            catch (Exception ex)
            {
                // Log error and return failure
                Log.Error($"Error updating part: {ex.Message}");
                return false;
            }
        }


        public async Task<List<DTOs.PartsUsed>> GetPartsByWorkOrder(int sheetID)
        {


            var partsUsedList = await _context.PartsUseds
                .Include(t => t.Inventory)  // Include the related Inventory
                    .ThenInclude(i => i.InventoryDocuments)  // Include the related InventoryDocuments
                .Where(t => t.SheetId == sheetID)
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
                    ReorderPoint = partUsed.Inventory.ReorderPoint,
                    InventoryDocuments = partUsed.Inventory.InventoryDocuments.Select(doc => new DTOs.InventoryDocument
                    {
                        InventoryDocumentId = doc.InventoryDocumentId,
                        FileName = doc.FileName,
                        FileURL = doc.FileUrl,
                        UploadDate = doc.UploadDate,
                        UploadedBy = doc.UploadedBy
                    }).ToList()
                }
            }).ToList();

            return partsUsedDTO;
        }


    }

}

