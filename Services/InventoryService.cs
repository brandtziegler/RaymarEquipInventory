using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;
using RaymarEquipmentInventory.Helpers;

namespace RaymarEquipmentInventory.Services
{
    public class InventoryService : IInventoryService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public InventoryService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }
        public string GetProductById(int id)
        {
            // Imagine this method actually does something useful.
            return $"Product details for product ID: {id}";
        }

        public string GetAllProducts()
        {
            // Again, let's pretend this returns actual product data.
            return "Returning all products... because why not?";
        }


        public async Task UpdateOrInsertInventoryAsync(List<InventoryData> inventoryDataList)
        {

            if (inventoryDataList == null || inventoryDataList.Count == 0)
            {
                return; // If there’s nothing to update or insert, we just move on.
            }

            try
            {
                var newInventoryCount = 0;
                foreach (var inventoryPart in inventoryDataList)
                {
                    var mappedInventory = MapDtoToModel(inventoryPart);
                    var existingInventory = await _context.InventoryData
                        .FirstOrDefaultAsync(i => i.QuickBooksInvId == inventoryPart.InventoryId); // Using async for efficiency

                    if (existingInventory != null)
                    {
                        // Update the existing record
                        existingInventory.ItemName = inventoryPart.Description;
                        existingInventory.QuickBooksInvId = inventoryPart.InventoryId;
                        existingInventory.ManufacturerPartNumber = inventoryPart.ManufacturerPartNumber;
                        existingInventory.Description = inventoryPart.Description;
                        existingInventory.Cost = inventoryPart.Cost;
                        existingInventory.SalesPrice = inventoryPart.SalesPrice;
                        existingInventory.ReorderPoint = inventoryPart.ReorderPoint;
                        existingInventory.OnHand = inventoryPart.OnHand;

                        // Update in the context
                        _context.InventoryData.Update(existingInventory);
                    }
                    else
                    {
                        // Insert new record into the context
                        await _context.InventoryData.AddAsync(mappedInventory);
                        newInventoryCount++;
                    }
                }

                await _context.SaveChangesAsync(); // Save all changes asynchronously
            }
            catch (Exception ex)
            {
                // Log the exception - make sure you replace this with your actual logging framework
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        public async Task<int> InsertInventoryAsync(string itemName = "", string itemDescription = "", string mfgPartNo = "")
        {
            if (!string.IsNullOrEmpty(itemName) && !string.IsNullOrEmpty(itemDescription))
            {
                try
                {
                    var mappedInventory = new InventoryDatum
                    {
                        ItemName = itemName,
                        Description = itemDescription,
                        ManufacturerPartNumber = mfgPartNo
                    };

                    // Insert new record into the context
                    await _context.InventoryData.AddAsync(mappedInventory);
                    await _context.SaveChangesAsync(); // Save all changes asynchronously

                    // Ensure that the InventoryID is populated after saving
                    var invId = mappedInventory.InventoryId;  // Assuming InventoryID is auto-generated
                    if (invId > 0)
                    {
                        return invId;  // Return the generated InventoryID
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception - replace with actual logging framework
                    Console.WriteLine($"An error occurred: {ex.Message}");
                    return 0;
                }
            }

            return 0; // Return 0 if the data is invalid or something went wrong
        }


        public async Task<List<InventoryData>> GetInventoryPartsFromQuickBooksAsync(bool doUpdate = false)
        {
            var inventoryParts = new List<InventoryData>();

            string otherQuery = "SELECT ID, Name, PartNumber, Description, PurchaseCost, Price, QuantityOnHand, ReorderPoint FROM Items WHERE Type = 'Inventory' AND QuantityOnHand >= 1";
            try
            {
                _quickBooksConnectionService.OpenConnection(); // This is assumed to be synchronous

                using (var cmd = new OdbcCommand(otherQuery, _quickBooksConnectionService.GetConnection()))
                {
                    using (var reader = await cmd.ExecuteReaderAsync()) // Use async version
                    {
                        while (await reader.ReadAsync()) // Use async version to read
                        {
                            inventoryParts.Add(new InventoryData
                            {
                                InventoryId = StringHelper.CleanString(reader["ID"].ToString() ?? ""),
                                ItemName = StringHelper.CleanString(reader["Name"].ToString() ?? ""),
                                ManufacturerPartNumber = StringHelper.CleanString(reader["PartNumber"].ToString() ?? ""),
                                Description = StringHelper.CleanString(reader["Description"].ToString() ?? "Desc"),
                                Cost = StringHelper.ParseDecimal(reader["PurchaseCost"] ?? 0),
                                SalesPrice = StringHelper.ParseDecimal(reader["Price"] ?? 0),
                                ReorderPoint = StringHelper.ParseInt(reader["ReorderPoint"]),
                                OnHand = StringHelper.ParseInt(reader["QuantityOnHand"] ?? 0),
                            });
                        }
                    }

                    _quickBooksConnectionService.CloseConnection(); // Assuming this is still synchronous

                    if (doUpdate)
                    {
                        await UpdateOrInsertInventoryAsync(inventoryParts); // Update or insert the inventory data
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Well, ain't that a kick in the teeth: {ex.Message}");
                // Handle logging or re-throw as needed
            }

            return inventoryParts;
        }




        public async Task<List<InventoryForDropdown>> GetDropdownInfo(string searchTerm)
        {
            var dropdownList = new List<InventoryForDropdown>();

            try
            {
                // Step 1: Basic filtering that can be translated to SQL
                IQueryable<InventoryDatum> query = _context.InventoryData
                    .Where(o => !o.ManufacturerPartNumber.Contains("TST")) // Exclude test items
                    .Where(item => !string.IsNullOrWhiteSpace(item.ItemName) &&
                                   !string.IsNullOrWhiteSpace(item.ManufacturerPartNumber));

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    // Split the search term into individual keywords
                    var keywords = searchTerm.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    // Apply keyword filter for each keyword
                    foreach (var keyword in keywords)
                    {
                        query = query.Where(item =>
                            EF.Functions.Like(item.ManufacturerPartNumber.ToLower(), $"%{keyword}%") ||
                            EF.Functions.Like(item.ItemName.ToLower(), $"%{keyword}%"));
                    }
                }

                // Step 2: Execute the query to get preliminary results from the database
                var existingInventory = await query.ToListAsync();

                // Step 3: Perform in-memory filtering using regex and sort by ItemName
                dropdownList = existingInventory
                    .Where(item =>
                        System.Text.RegularExpressions.Regex.IsMatch(item.ItemName, @"^[a-zA-Z0-9\s]+$") &&
                        System.Text.RegularExpressions.Regex.IsMatch(item.ManufacturerPartNumber, @"^[a-zA-Z0-9\s]+$"))
                    .Select(item => new InventoryForDropdown
                    {
                        QuickBooksInvId = item.QuickBooksInvId,
                        ItemName = item.ItemName,
                        PartNumber = item.ManufacturerPartNumber,
                        QtyAvailable = item.OnHand ?? 0
                    })
                    .OrderBy(item => item.ItemName) // Sort by ItemName alphabetically
                    .ToList();

                return dropdownList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Well, ain't that a kick in the teeth: {ex.Message}");
                throw;
            }
        }




        public async Task<List<InventoryForDropdown>> GetAllInventoryItems()
        {
            var dropdownList = new List<InventoryForDropdown>();

            try
            {
                // Pull all the relevant inventory data from SQL Server
                var existingInventory = await _context.InventoryData.ToListAsync();

                // Map the SQL Server data to the InventoryForDropdown object
                dropdownList = existingInventory.Select(item => new InventoryForDropdown
                {
                    QuickBooksInvId = item.QuickBooksInvId, // Pull the QuickBooks Inventory ID directly
                    ItemName = item.ItemName, 
                    PartNumber = item.ManufacturerPartNumber,
                    QtyAvailable = item.OnHand ?? 0 // Get the quantity available
                }).ToList(); // Convert to list

                // Return the dropdown list to the controller
                return dropdownList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Well, ain't that a kick in the teeth: {ex.Message}");
                // Handle logging or re-throw as needed
                throw;
            }
        }



        private Models.InventoryDatum MapDtoToModel(DTOs.InventoryData inventoryPart)
        {
            return new Models.InventoryDatum
            {
                QuickBooksInvId = inventoryPart.InventoryId, // Map the ID
                ItemName = inventoryPart.ItemName,
                ManufacturerPartNumber = inventoryPart.ManufacturerPartNumber,
                Description = inventoryPart.Description,
                Cost = inventoryPart.Cost,
                SalesPrice = inventoryPart.SalesPrice,
                ReorderPoint = inventoryPart.ReorderPoint,
                OnHand = inventoryPart.OnHand
                // Map additional fields as necessary
            };
        }
  

    }

}

