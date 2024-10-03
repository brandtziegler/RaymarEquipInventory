using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;
using System.Data;
using System.Reflection.PortableExecutable;
using RaymarEquipmentInventory.Helpers;

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
            //var fieldNames = GetCustomerFieldNames();
            // WHERE (FullName LIKE 'Sarj%' OR ParentName LIKE 'Sarj%') AND 
            string query = "SELECT ID, ParentID, ParentName, Name, FullName, FirstName, LastName, JobDescription, AccountNumber, Phone, Email, Notes, JobStatus, Company, Sublevel, BillingAddress, IsActive FROM Customers" +
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
                            var activeOrNot = StringHelper.CleanString(reader["IsActive"].ToString() ?? "");

                            var customer = new CustomerData
                            {
                                ID = StringHelper.CleanString(reader["ID"].ToString() ?? ""),
                                ParentID = StringHelper.CleanString(reader["ParentID"].ToString() ?? ""),
                                ParentName = StringHelper.CleanString(reader["ParentName"].ToString() ?? ""),
                                Name = StringHelper.CleanString(reader["Name"].ToString() ?? ""),
                                FullName = StringHelper.CleanString(reader["FullName"].ToString() ?? ""),
                                FirstName = StringHelper.CleanString(reader["FirstName"].ToString() ?? ""),
                                LastName = StringHelper.CleanString(reader["LastName"].ToString() ?? ""),
                                Description = StringHelper.CleanString(reader["JobDescription"].ToString() ?? ""),
                                FullAddress = StringHelper.CleanString(reader["BillingAddress"].ToString() ?? ""),
                                AccountNumber = StringHelper.CleanString(reader["AccountNumber"].ToString() ?? ""),
                                Phone = StringHelper.CleanString(reader["Phone"].ToString() ?? ""),
                                Email = StringHelper.CleanString(reader["Email"].ToString() ?? ""),
                                Notes = StringHelper.CleanString(reader["Notes"].ToString() ?? ""),
                                JobStatus = StringHelper.CleanString(reader["JobStatus"].ToString() ?? ""),
                                Company = StringHelper.CleanString(reader["Company"].ToString() ?? ""),  // Fixed to match the column
                                SubLevelId = StringHelper.ParseInt(reader["Sublevel"]) ?? 0,
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

        //private List<string> GetCustomerFieldNames()
        //{
        //    var fieldNames = new List<string>();

        //    try
        //    {
        //        _quickBooksConnectionService.OpenConnection(); // Ensure the connection is open

        //        using (var connection = _quickBooksConnectionService.GetConnection())
        //        {
        //            // Get the schema for the Customers table
        //            DataTable schemaTable = connection.GetSchema("Columns", new string[] { null, null, "Customers", null });

        //            foreach (DataRow row in schemaTable.Rows)
        //            {
        //                string columnName = row["COLUMN_NAME"].ToString();
        //                fieldNames.Add(columnName);
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        // Handle any exceptions
        //        throw;
        //    }
        //    finally
        //    {
        //        _quickBooksConnectionService.CloseConnection(); // Ensure the connection is closed
        //    }

        //    return fieldNames;
        //}
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
            existingCustomer.ParentName = StringHelper.NullIfEmpty(customer.ParentName);
            existingCustomer.CustomerName = StringHelper.NullIfEmpty(customer.Name);
            existingCustomer.FullName = StringHelper.NullIfEmpty(customer.FullName);
            existingCustomer.FirstName = StringHelper.NullIfEmpty(customer.FirstName);
            existingCustomer.LastName = StringHelper.NullIfEmpty(customer.LastName);
            existingCustomer.Description = StringHelper.NullIfEmpty(customer.Description);
            existingCustomer.AccountNumber = StringHelper.NullIfEmpty(customer.AccountNumber);
            existingCustomer.Phone = StringHelper.NullIfEmpty(customer.Phone);
            existingCustomer.Email = StringHelper.NullIfEmpty(customer.Email);
            existingCustomer.Notes = StringHelper.NullIfEmpty(customer.Notes);
            existingCustomer.JobStatus = StringHelper.NullIfEmpty(customer.JobStatus);
            existingCustomer.Company = StringHelper.NullIfEmpty(customer.Company);
            existingCustomer.SubLevelId = customer.SubLevelId;
            existingCustomer.FullAddress = customer.FullAddress;
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
                FullAddress = customer.FullAddress
            };
        }




    }

}

