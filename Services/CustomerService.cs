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
            var flatCustomers = new List<CustomerData>();
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
                            flatCustomers.Add(customer);
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
            if (doUpdate)
            {
                await UpdateOrInsertCustAsync(flatCustomers);
            }
            // Return the root-level customers with their children attached
            return flatCustomers;
        }

        public async Task UpdateOrInsertCustAsync(List<CustomerData> customerDataList)
        {
            if (customerDataList == null || customerDataList.Count == 0)
            {
                return; // No customers, nothing to do.
            }

            try
            {
                // Step 1: Insert or update all customers without setting the ParentID
                await InsertCustomersWithoutParentIDAsync(customerDataList);

                // Step 2: Save all customers to the database (with ParentID temporarily set to null)
                await _context.SaveChangesAsync(); // Parents and children saved without ParentID

                // Step 3: Now that all customers are in the database, update ParentID correctly
                await UpdateCustomerParentIDsAsync(customerDataList);

                // Step 4: Save the changes again after updating ParentID references
                await _context.SaveChangesAsync(); // Save all ParentID updates
            }
            catch (Exception ex)
            {
                // Replace this with actual logging
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        // Step 1: Insert all customers with ParentID set to null
        private async Task InsertCustomersWithoutParentIDAsync(List<CustomerData> customerList)
        {
            foreach (var customer in customerList)
            {
                var existingCustomer = await _context.Customers
                    .FirstOrDefaultAsync(i => i.Id == customer.ID); // Find existing customer

                if (existingCustomer != null)
                {
                    // If customer exists, update it (but leave ParentID as null for now)
                    UpdateExistingCustomerWithoutParent(existingCustomer, customer);
                    _context.Customers.Update(existingCustomer);
                }
                else
                {
                    // Insert new customer with ParentID set to null
                    var newCustomer = MapDtoToModelWithoutParent(customer);
                    await _context.Customers.AddAsync(newCustomer);
                }
            }
        }

        // Step 3: Update the ParentID for all customers now that they exist in the database
        private async Task UpdateCustomerParentIDsAsync(List<CustomerData> customerList)
        {
            foreach (var customer in customerList)
            {
                var existingCustomer = await _context.Customers
                    .FirstOrDefaultAsync(i => i.Id == customer.ID); // Find existing customer

                if (existingCustomer != null && !string.IsNullOrEmpty(customer.ParentID))
                {
                    // Only update the ParentID now that all customers are inserted
                    existingCustomer.ParentId = customer.ParentID;
                    _context.Customers.Update(existingCustomer);
                }
            }
        }

        // Helper method to update an existing customer without touching ParentID
        private void UpdateExistingCustomerWithoutParent(Models.Customer existingCustomer, DTOs.CustomerData customer)
        {
            existingCustomer.Id = customer.ID;
            existingCustomer.ParentId = null; // Temporarily set to null
            existingCustomer.ParentName = NullIfEmpty(customer.ParentName);
            existingCustomer.CustomerName = NullIfEmpty(customer.Name);
            existingCustomer.FullName = NullIfEmpty(customer.FullName);
            existingCustomer.FirstName = NullIfEmpty(customer.FirstName);
            existingCustomer.LastName = NullIfEmpty(customer.LastName);
            existingCustomer.Description = NullIfEmpty(customer.Description);
            existingCustomer.AccountNumber = NullIfEmpty(customer.AccountNumber);
            existingCustomer.Phone = NullIfEmpty(customer.Phone);
            existingCustomer.Email = NullIfEmpty(customer.Email);
            existingCustomer.Notes = NullIfEmpty(customer.Notes);
            existingCustomer.JobStatus = NullIfEmpty(customer.JobStatus);
            existingCustomer.Company = NullIfEmpty(customer.Company);
            existingCustomer.SubLevelId = customer.SubLevelId;
        }

        // Mapping function without ParentID (for inserting initially)
        private Models.Customer MapDtoToModelWithoutParent(DTOs.CustomerData customer)
        {
            return new Models.Customer
            {
                Id = customer.ID,
                ParentId = null, // Set ParentID to null during initial insert
                ParentName = customer.ParentName,
                CustomerName = customer.Name,
                FullName = customer.FullName,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Description = customer.Description,
                AccountNumber = customer.AccountNumber,
                Phone = customer.Phone,
                Email = customer.Email,
                Notes = customer.Notes,
                JobStatus = customer.JobStatus,
                Company = customer.Company,
                SubLevelId = customer.SubLevelId,
            };
        }


        private static string CleanString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Remove non-printable characters and trim whitespace
            return new string(input.Where(c => !char.IsControl(c)).ToArray()).Trim();
        }
        public static string? NullIfEmpty(string input)
        {
            return string.IsNullOrEmpty(input) ? null : input;
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

