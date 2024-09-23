using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;


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

        public async Task<List<InventoryData>> GetInventoryPartsFromQuickBooksAsync(bool doUpdate = false)
        {
            var inventoryParts = new List<InventoryData>();

            string otherQuery = "SELECT TOP 1500 ID, Name, PartNumber, Description, PurchaseCost, Price, QuantityOnHand, ReorderPoint FROM Items WHERE Type = 'Inventory' AND Price > 0.1 AND QuantityOnHand >= 1";
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
                                InventoryId = CleanString(reader["ID"].ToString() ?? ""),
                                ItemName = CleanString(reader["Name"].ToString() ?? ""),
                                ManufacturerPartNumber = CleanString(reader["PartNumber"].ToString() ?? ""),
                                Description = CleanString(reader["Description"].ToString() ?? "Desc"),
                                Cost = ParseDecimal(reader["PurchaseCost"] ?? 0),
                                SalesPrice = ParseDecimal(reader["Price"] ?? 0),
                                ReorderPoint = ParseInt(reader["ReorderPoint"]),
                                OnHand = ParseInt(reader["QuantityOnHand"] ?? 0),
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

        public async Task<List<InventoryForDropdown>> GetDropdownInfo()
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
                    ItemNameWithPartNum = $"{item.ManufacturerPartNumber}:{item.ItemName}", // Combine part number and item name
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
        private static string CleanString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Remove non-printable characters and trim whitespace
            return new string(input.Where(c => !char.IsControl(c)).ToArray()).Trim();
        }

        // Method to safely parse decimals
        private static decimal? ParseDecimal(object input)
        {
            if (input == null || input == DBNull.Value)
                return null;

            if (decimal.TryParse(input.ToString(), out decimal result))
                return result;

            return null; // Or return a default value if needed
        }

        // Method to safely parse integers
        private static int? ParseInt(object input)
        {
            if (input == null || input == DBNull.Value)
                return null;

            if (int.TryParse(input.ToString(), out int result))
                return result;

            return null; // Or return a default value if needed
        }

    }

}

