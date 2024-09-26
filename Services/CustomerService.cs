using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;
using System.Data;
using System.Reflection.PortableExecutable;


namespace RaymarEquipmentInventory.Services
{
    public class CustomerService : ICustomerService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public CustomerService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }
       
        public async Task<List<CustomerData>> GetCustomersFromQuickBooksAsnyc(bool doUpdate = false)
        {
            var custList = new List<CustomerData>();

            string otherQuery = "SELECT ID, Name FROM Customers";
            var columnList = new List<string>();

            string columnQuery = "SELECT * FROM Customers";

            try
            {
                _quickBooksConnectionService.OpenConnection(); // This is assumed to be synchronous
                                                               // Get the column names dynamically
                using (var describeCmd = new OdbcCommand(columnQuery, _quickBooksConnectionService.GetConnection()))
                {
                    using (var describeReader = await describeCmd.ExecuteReaderAsync()) // Use async version
                    {
                        var schemaTable = describeReader.GetSchemaTable();
                 

                        // Step 3: Add each column name from the schema table to the columnList
                        foreach (DataRow row in schemaTable.Rows)
                        {
                            columnList.Add(row["ColumnName"].ToString());
                        }
                    }
                }
                string columnString = string.Join(", ", columnList);

                using (var cmd = new OdbcCommand(otherQuery, _quickBooksConnectionService.GetConnection()))
                {
                    using (var reader = await cmd.ExecuteReaderAsync()) // Use async version
                    {
                        while (await reader.ReadAsync()) // Use async version to read
                        {
                            custList.Add(new CustomerData
                            {
                                ID = CleanString(reader["ID"].ToString() ?? ""),
                                Name = CleanString(reader["Name"].ToString() ?? ""),
                                //Description = CleanString(reader["Description"].ToString() ?? "")
                            });
                        }
                    }

                    _quickBooksConnectionService.CloseConnection(); // Assuming this is still synchronous

                    //if (doUpdate)
                    //{
                    //    await UpdateOrInsertInventoryAsync(inventoryParts); // Update or insert the inventory data
                    //}
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Well, ain't that a kick in the teeth: {ex.Message}");
                // Handle logging or re-throw as needed
            }

            return custList;
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

