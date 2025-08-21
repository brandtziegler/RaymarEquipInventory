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
                InventoryID = partUsed.InventoryId ?? 0,
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
            if (partsUsedDto.PartUsedId == 0 || partsUsedDto.SheetId == null || partsUsedDto.InventoryID == 0 || partsUsedDto.QtyUsed == null)
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
                partUsedEntity.InventoryId = partsUsedDto.InventoryID;
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

        public async Task<bool> ClearPartsUsedAsync(int sheetId, CancellationToken ct = default)
        {
            try
            {
                // Step 1: Get all PartUsedIds for the given SheetId
                var partUsedIds = await _context.PartsUseds
                    .Where(p => p.SheetId == sheetId)
                    .Select(p => p.PartUsedId)
                    .ToListAsync();

                if (!partUsedIds.Any())
                {
                    Log.Information($"🧹 No PartsUsed entries found for SheetID {sheetId}. Nothing to clear.");
                    return true;
                }

                // Step 2: Delete associated PartsDocuments
                var docsToDelete = _context.PartsDocuments
                    .Where(d => partUsedIds.Contains(d.PartUsedId));
                _context.PartsDocuments.RemoveRange(docsToDelete);

                // Step 3: Delete PartsUseds
                var partsToDelete = _context.PartsUseds
                    .Where(p => p.SheetId == sheetId);
                _context.PartsUseds.RemoveRange(partsToDelete);

                // Final save
                await _context.SaveChangesAsync();

                Log.Information($"✅ Cleared {partUsedIds.Count} PartsUsed entries and associated PartsDocuments for SheetID {sheetId}.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Failed to clear PartsUsed/PartsDocuments for SheetID {sheetId}.");
                return false;
            }
        }



        public async Task<int> InsertPartsUsedBulkAsync(
    IEnumerable<DTOs.PartsUsed> items,
    CancellationToken ct = default)
        {
            var list = items?.ToList() ?? new List<DTOs.PartsUsed>();
            if (list.Count == 0) return 0;

            // Keep dto<->entity pairing so we can attach docs after IDs are generated
            var pairs = new List<(DTOs.PartsUsed dto, Models.PartsUsed entity)>(list.Count);

            foreach (var dto in list)
            {
                if (!(dto.SheetId.HasValue && dto.SheetId.Value > 0)) continue;

                var entity = new Models.PartsUsed
                {
                    QtyUsed = dto.QtyUsed ?? 0,
                    Notes = dto.Notes?.Trim() ?? "",
                    SheetId = dto.SheetId.Value,
                    InventoryId = dto.InventoryID ?? 7,
                    PartNumber = dto.PartNumber,
                    Description = dto.Description,
                    UploadDate = DateTime.Now,
                    UploadedBy = "iPad App",
                    Deleted = false
                };

                pairs.Add((dto, entity));
            }

            if (pairs.Count == 0) return 0;

            // Insert all PartsUsed in one shot
            var prev = _context.ChangeTracker.AutoDetectChangesEnabled;
            try
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                _context.PartsUseds.AddRange(pairs.Select(p => p.entity));
                await _context.SaveChangesAsync(ct); // IDs generated here
            }
            finally
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = prev;
            }

            // Build all PartsDocuments now that PartUsedId is known
            var docEntities = new List<Models.PartsDocument>();
            foreach (var (dto, entity) in pairs)
            {
                if (dto.PartsDocs == null || dto.PartsDocs.Count == 0) continue;

                foreach (var doc in dto.PartsDocs)
                {
                    docEntities.Add(new Models.PartsDocument
                    {
                        PartUsedId = entity.PartUsedId,
                        FileName = doc.FileName,
                        Description = doc.Description ?? "UNKNOWN",
                        UploadedBy = "iPad App",
                        UploadDate = DateTime.Now
                    });
                }
            }

            if (docEntities.Count > 0)
            {
                _context.PartsDocuments.AddRange(docEntities);
                await _context.SaveChangesAsync(ct);
            }

            Log.Information("✅ Bulk inserted {Count} PartsUsed and {Docs} PartsDocuments.", pairs.Count, docEntities.Count);
            return pairs.Count;
        }

        public async Task<bool> InsertPartsUsedAsync(DTOs.PartsUsed dto, CancellationToken ct = default)
        {
            try
            {
                // Basic validation
                if (dto.SheetId is null || dto.SheetId <= 0)
                {
                    Log.Warning("SheetId is required and must be valid.");
                    return false;
                }

                var newEntry = new Models.PartsUsed
                {
                    QtyUsed = dto.QtyUsed ?? 0,
                    Notes = dto.Notes?.Trim() ?? "",
                    SheetId = dto.SheetId ?? 0,
                    InventoryId = dto.InventoryID ?? 7,
                    PartNumber = dto.PartNumber,  // optional, could be filled later
                    Description = dto.Description, // optional, could be filled later
                    UploadDate = DateTime.Now, // Server decides this
                    UploadedBy = "iPad App",     // Optional default
                    Deleted = false              // Not deleted by default
                };

                await _context.PartsUseds.AddAsync(newEntry);
                await _context.SaveChangesAsync();

                //add parts documents...
                if (dto.PartsDocs != null && dto.PartsDocs.Count > 0)
                {
                    foreach (var doc in dto.PartsDocs)
                    {
                        var newDoc = new Models.PartsDocument
                        {
                            PartUsedId = newEntry.PartUsedId, // Must use newEntry's generated ID
                            FileName = doc.FileName,
                            Description = doc.Description ?? "UNKNOWN",
                            UploadedBy = "iPad App",
                            UploadDate = DateTime.Now
                        };

                        await _context.PartsDocuments.AddAsync(newDoc);
                    }
                }

                await _context.SaveChangesAsync();

                Log.Information($"Inserted part usage for InventoryID {dto.InventoryID}, Qty: {dto.QtyUsed}");
                return true;
            }
            catch (Exception ex)
            {
                var fullError = ex.InnerException != null
                    ? $"{ex.Message} | Inner: {ex.InnerException.Message}"
                    : ex.Message;

                Log.Error(ex, $"❌ Failed to insert part usage. Details: {fullError}");

                return false;
            }
        }

        public async Task<int> GetPartsCountByWorkOrder(
    int sheetID,
    string itemName = null,
    int? qtyUsedMin = null,
    int? qtyUsedMax = null,
    string manufacturerPartNumber = null)
        {
            var partsUsedQuery = _context.PartsUseds.Where(p => p.SheetId == sheetID);
            partsUsedQuery = ApplyFilters(partsUsedQuery, itemName, qtyUsedMin, qtyUsedMax, manufacturerPartNumber);
            return await partsUsedQuery.CountAsync();
        }

        public async Task<List<DTOs.PartsUsed>> GetPartsByWorkOrder(
            int sheetID,
            int pageNumber = 0,
            int pageSize = 0,
            string itemName = null,
            int? qtyUsedMin = null,
            int? qtyUsedMax = null,
            string manufacturerPartNumber = null,
            string sortBy = "itemName", // Default sort field
            string sortDirection = "asc" // Default sort direction
        )
        {
            // Start building the base query for Models.PartsUsed
            var partsUsedQuery = _context.PartsUseds
                .Include(p => p.Inventory)
                    .ThenInclude(i => i.InventoryDocuments)
                    .ThenInclude(d => d.DocumentType)
                .Where(p => p.SheetId == sheetID);

            // Apply filtering
            partsUsedQuery = ApplyFilters(partsUsedQuery, itemName, qtyUsedMin, qtyUsedMax, manufacturerPartNumber);

            // Apply sorting
            partsUsedQuery = ApplySorting(partsUsedQuery, sortBy, sortDirection);

            // Apply pagination if required
            if (pageNumber > 0 && pageSize > 0)
            {
                partsUsedQuery = ApplyPagination(partsUsedQuery, pageNumber, pageSize);
            }

            // Execute query and retrieve Models.PartsUsed entities
            var partsUsedList = await partsUsedQuery.ToListAsync();

            // Map Models.PartsUsed entities to DTOs.PartsUsed
            return await MapToDto(partsUsedList);
        }

        // Private method for sorting Models.PartsUsed
        private IQueryable<Models.PartsUsed> ApplySorting(
            IQueryable<Models.PartsUsed> query,
            string sortBy,
            string sortDirection)
        {
            // Default to ascending order unless "desc" is specified
            bool isDescending = sortDirection.ToLower() == "desc";

            // Apply sorting based on specified field
            query = sortBy.ToLower() switch
            {
                "qtyused" => isDescending ? query.OrderByDescending(p => p.QtyUsed) : query.OrderBy(p => p.QtyUsed),
                "manufacturerpartnumber" => isDescending ? query.OrderByDescending(p => p.Inventory.ManufacturerPartNumber) : query.OrderBy(p => p.Inventory.ManufacturerPartNumber),
                "itemname" => isDescending ? query.OrderByDescending(p => p.Inventory.ItemName) : query.OrderBy(p => p.Inventory.ItemName),
                _ => isDescending ? query.OrderByDescending(p => p.Inventory.ItemName) : query.OrderBy(p => p.Inventory.ItemName) // Default sort
            };

            return query;
        }


        // Private method for filtering Models.PartsUsed
        private IQueryable<Models.PartsUsed> ApplyFilters(
            IQueryable<Models.PartsUsed> query,
            string itemName,
            int? qtyUsedMin,
            int? qtyUsedMax,
            string manufacturerPartNumber)
        {
            if (!string.IsNullOrEmpty(itemName))
            {
                query = query.Where(p => p.Inventory.ItemName.Contains(itemName));
            }

            if (qtyUsedMin.HasValue)
            {
                query = query.Where(p => p.QtyUsed >= qtyUsedMin.Value);
            }

            if (qtyUsedMax.HasValue)
            {
                query = query.Where(p => p.QtyUsed <= qtyUsedMax.Value);
            }

            if (!string.IsNullOrEmpty(manufacturerPartNumber))
            {
                query = query.Where(p => p.Inventory.ManufacturerPartNumber.Contains(manufacturerPartNumber));
            }

            return query;
        }

        // Private method for pagination of Models.PartsUsed
        private IQueryable<Models.PartsUsed> ApplyPagination(IQueryable<Models.PartsUsed> query, int pageNumber, int pageSize)
        {
            int skipCount = (pageNumber - 1) * pageSize;
            return query.Skip(skipCount).Take(pageSize);
        }

        // Private method to map Models.PartsUsed to DTOs.PartsUsed
        private async Task<List<DTOs.PartsUsed>> MapToDto(List<Models.PartsUsed> partsUsedList)
        {
            // Fetch placeholder document details
            var placeholderDocument = await _context.PlaceholderDocuments
                .Include(d => d.DocumentType)
                .FirstOrDefaultAsync();

            // Map each Models.PartsUsed to DTOs.PartsUsed
            return partsUsedList.Select(partUsed => new DTOs.PartsUsed
            {
                PartUsedId = partUsed.PartUsedId,
                InventoryID = partUsed.InventoryId ?? 0,
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
                    InventoryDocuments = partUsed.Inventory.InventoryDocuments.Any()
                        ? partUsed.Inventory.InventoryDocuments.Select(doc => new DTOs.InventoryDocument
                        {
                            InventoryDocumentId = doc.InventoryDocumentId,
                            FileName = doc.FileName,
                            FileURL = doc.FileUrl,
                            DocType = new DTOs.DocumentType
                            {
                                DocumentTypeId = doc.DocumentType.DocumentTypeId,
                                DocumentTypeName = doc.DocumentType.DocumentTypeName,
                                MimeType = doc.DocumentType.MimeType
                            },
                            UploadDate = doc.UploadDate,
                            UploadedBy = doc.UploadedBy
                        }).ToList()
                        : new List<DTOs.InventoryDocument>
                        {
                    new DTOs.InventoryDocument
                    {
                        InventoryDocumentId = 0,
                        FileName = placeholderDocument?.FileName ?? "",
                        FileURL = placeholderDocument?.FileUrl ?? "",
                        DocType = new DTOs.DocumentType
                        {
                            DocumentTypeId = placeholderDocument?.DocumentType.DocumentTypeId ?? 0,
                            DocumentTypeName = placeholderDocument?.DocumentType.DocumentTypeName ?? "",
                            MimeType = placeholderDocument?.DocumentType.MimeType ?? ""
                        },
                        UploadDate = placeholderDocument?.UploadDate ?? DateTime.MinValue,
                        UploadedBy = "System"
                    }
                        }
                }
            }).ToList();
        }


    }

}

