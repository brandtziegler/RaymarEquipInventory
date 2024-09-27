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
            var allCustomers = new Dictionary<string, CustomerData>();
            var parentCustomers = new List<CustomerData>();
            // WHERE (FullName LIKE 'Sarj%' OR ParentName LIKE 'Sarj%') AND 
            string query = "SELECT ID, ParentID, ParentName, Name, FullName, FirstName, LastName, JobDescription, AccountNumber, Phone, Email, Notes, JobStatus, Company, Sublevel, IsActive FROM Customers" +
                           " WHERE IsActive = 1 ORDER BY Sublevel, FullName";

            try
            {
                _quickBooksConnectionService.OpenConnection(); // Make sure you manage this connection properly

                using (var cmd = new OdbcCommand(query, _quickBooksConnectionService.GetConnection()))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var activeOrNot = CleanString(reader["IsActive"].ToString() ?? "");

                            var customer = new CustomerData
                            {
                                ID = CleanString(reader["ID"].ToString() ?? ""),
                                ParentID = CleanString(reader["ParentID"].ToString() ?? ""),
                                ParentName = CleanString(reader["ParentName"].ToString() ?? ""),
                                Name = CleanString(reader["Name"].ToString() ?? ""),
                                FullName = CleanString(reader["FullName"].ToString() ?? ""),
                                FirstName = CleanString(reader["FirstName"].ToString() ?? ""),
                                LastName = CleanString(reader["LastName"].ToString() ?? ""),
                                Description = CleanString(reader["JobDescription"].ToString() ?? ""),
                                AccountNumber = CleanString(reader["AccountNumber"].ToString() ?? ""),
                                Phone = CleanString(reader["Phone"].ToString() ?? ""),
                                Email = CleanString(reader["Email"].ToString() ?? ""),
                                Notes = CleanString(reader["Notes"].ToString() ?? ""),
                                JobStatus = CleanString(reader["JobStatus"].ToString() ?? ""),
                                Company = CleanString(reader["Company"].ToString() ?? ""),  // Fixed to match the column
                                SubLevelId = ParseInt(reader["Sublevel"]) ?? 0,
                                IsActive = activeOrNot == "1"
                            };

                            allCustomers[customer.ID] = customer;

                            if (string.IsNullOrEmpty(customer.ParentID))
                            {
                                // No parent, so it's a root customer
                                parentCustomers.Add(customer);
                            }
                        }
                    }
                }

                // Assign children to parents
                foreach (var customer in allCustomers.Values)
                {
                    if (!string.IsNullOrEmpty(customer.ParentID) && allCustomers.ContainsKey(customer.ParentID))
                    {
                        var parent = allCustomers[customer.ParentID];
                        parent.Children.Add(customer);
                    }
                }
            }
            catch (OdbcException ex)
            {
                Console.WriteLine($"Well, ain't that a kick in the teeth: {ex.Message}");
                // Add appropriate logging here
            }
            finally
            {
                _quickBooksConnectionService.CloseConnection(); // Close the connection here to avoid leaving it open
            }

            // Return the root-level customers with their children attached
            return parentCustomers;
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

