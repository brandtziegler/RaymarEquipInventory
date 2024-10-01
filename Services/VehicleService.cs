using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;


namespace RaymarEquipmentInventory.Services
{
    public class VehicleService : IVehicleService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public VehicleService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }
        //public string GetProductById(int id)
        //{
        //    // Imagine this method actually does something useful.
        //    return $"Product details for product ID: {id}";
        //}

        //public string GetAllProducts()
        //{
        //    // Again, let's pretend this returns actual product data.
        //    return "Returning all products... because why not?";
        //}


        public async Task UpdateOrInsertVehiclesAsync(List<Vehicle> vehicleList)
        {

            if (vehicleList == null || vehicleList.Count == 0)
            {
                return; // If there’s nothing to update or insert, we just move on.
            }

            try
            {
                var newVehicleCount = 0;
                foreach (var vehicle in vehicleList)
                {
                    var mappedVehicle = MapDtoToModel(vehicle);
                    var existingVehicle = await _context.VehicleData
                        .FirstOrDefaultAsync(i => i.SamsaraVehicleId == vehicle.SamsaraVehicleID); // Using async for efficiency

                    if (existingVehicle != null)
                    {
                        // Update the existing record
                        existingVehicle.VehicleName = vehicle.VehicleName;
                        existingVehicle.VehicleVin = vehicle.VehicleVIN;
                        // Update in the context
                        _context.VehicleData.Update(existingVehicle);
                    }
                    else
                    {
                        // Insert new record into the context
                        await _context.VehicleData.AddAsync(mappedVehicle);
                        newVehicleCount++;
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

        public async Task<List<InventoryForDropdown>> GetAllPartsItems()
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



        private Models.VehicleDatum MapDtoToModel(DTOs.Vehicle curVehicle)
        {
            return new Models.VehicleDatum
            {
                SamsaraVehicleId = curVehicle.SamsaraVehicleID,
                VehicleName = curVehicle.VehicleName,
                VehicleVin = curVehicle.VehicleVIN,
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

