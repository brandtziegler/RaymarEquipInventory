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
using System.Text;

namespace RaymarEquipmentInventory.Services
{



    static class RowVer
    {
        public static string ToHex(byte[] rv) =>
            "0x" + BitConverter.ToString(rv).Replace("-", "");
        public static byte[] Parse(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return new byte[8];
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
            if (hex.Length % 2 != 0) hex = "0" + hex;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            // left-pad/truncate to 8 bytes
            if (bytes.Length == 8) return bytes;
            var out8 = new byte[8];
            Array.Copy(bytes, 0, out8, Math.Max(0, 8 - bytes.Length), Math.Min(8, bytes.Length));
            return out8;
        }
    }

    public class CustomerService : ICustomerService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly IQBWCResponseHandler _qbwcResponseHandler;
        private readonly ICustomerImportService _customerImport;
        private readonly RaymarInventoryDBContext _context;
        public CustomerService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context, IQBWCResponseHandler qBWCResponseHandler, ICustomerImportService customerImport)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _qbwcResponseHandler = qBWCResponseHandler;
            _context = context;
            _customerImport = customerImport;
        }



        public async Task<object> ImportCustomersFromQbwcXmlAsync(
        Stream xmlStream,
        bool insert = true,
        bool promote = false,
        bool fullRefresh = false,
        CancellationToken ct = default)
        {
            using var reader = new StreamReader(xmlStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            var qbxml = await reader.ReadToEndAsync();

            var runId = Guid.NewGuid();

            // Parse the uploaded qbXML → same code path as QBWC responses
            var parsed = await _qbwcResponseHandler.HandleReceiveAsync(runId, qbxml, null, null, ct);
            var custCount = parsed.Customers?.Count ?? 0;   // customers only here
                                                            // (If you ever send an inventory file by mistake, parser would put those under InventoryItems.) :contentReference[oaicite:4]{index=4}

            int inserted = 0;
            if (insert && custCount > 0)
            {
                inserted = await _customerImport.BulkInsertCustomersAsync(runId, parsed.Customers, ct);   // → dbo.CustomerBackup (TRUNCATE + bulk) :contentReference[oaicite:5]{index=5}
                await _customerImport.SyncCustomerDataAsync(runId);                                  // resolve parents/paths/effective-active (in backup) :contentReference[oaicite:6]{index=6}
            }

            int promoted = 0;
            if (promote)
            {
                // Call your stored proc when ready (from earlier step):
                // promoted = await _context.Database.ExecuteSqlRawAsync(
                //    "EXEC dbo.SyncCustomerFromBackup @FullRefresh = {0}", fullRefresh ? 1 : 0, ct);
            }

            return new
            {
                runId,
                parsedCustomers = custCount,
                insertedToBackup = inserted,
                promotedToLive = promoted
            };
        }


        public async Task<List<CustomerData>> GetCustomersFromQuickBooksAsnyc(bool doUpdate = false)
        {
            var allCustomers = new Dictionary<string, CustomerData>();
            var parentCustomers = new List<CustomerData>();
            var flatCustomers = new List<CustomerData>();
            //var fieldNames = GetCustomerFieldNames();
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

        public async Task<CustomerData> GetCustomerByID(int custID)
        {
            try
            {
                // Step 1: Fetch the customer without fetching the children yet
                var existingCustomer = await _context.Customers
                    .Where(i => i.CustomerId == custID)
                    .Select(c => new CustomerData
                    {
                        CustomerID = c.CustomerId,
                        ID = c.Id,
                        ParentID = c.ParentId,
                        ParentName = c.ParentName,
                        Name = c.CustomerName,
                        FullName = c.FullName,
                        FirstName = c.FirstName,
                        LastName = c.LastName,
                        Description = c.Description,
                        AccountNumber = c.AccountNumber,
                        Phone = c.Phone,
                        Email = c.Email,
                        Notes = c.Notes,
                        Company = c.Company,
                        SubLevelId = c.SubLevelId,
                        FullAddress = c.FullAddress,
                       
                        // Leave Children empty for now; we'll populate it after fetching the customer
                        Children = new List<CustomerData>()
                    })
                    .FirstOrDefaultAsync();

                if (existingCustomer == null)
                {
                    return null; // No customer found
                }

                // Step 2: Now fetch the children asynchronously after the customer is fetched
                existingCustomer.Children = await GetChildren(existingCustomer.ID);

                return existingCustomer;
            }
            catch (Exception ex)
            {
                // Log the exception using Serilog
                Log.Error(ex, "An error occurred while fetching customer with ID {CustomerID}", custID);

                // Optionally, you can rethrow the exception or return null
                return null;
            }
        }


        public async Task<List<CustomerData>> GetAllCustomers()
        {
            List<CustomerData> customerDataList = new List<CustomerData>();

            try
            {
                // Step 1: Get all parent customers (SubLevelId = 0)
                var parentCustomers = await _context.Customers
                    .Where(c => c.SubLevelId == 0)
                    .ToListAsync();

                // Step 2: Add each parent and fetch its children recursively
                foreach (var parent in parentCustomers)
                {
                    var parentData = new CustomerData
                    {
                        CustomerID = parent.CustomerId,
                        ID = parent.Id,
                        ParentID = parent.ParentId,
                        ParentName = parent.ParentName,
                        Name = parent.CustomerName,
                        FullName = parent.FullName,
                        FirstName = parent.FirstName,
                        LastName = parent.LastName,
                        Description = parent.Description,
                        AccountNumber = parent.AccountNumber,
                        Phone = parent.Phone,
                        Email = parent.Email,
                        Notes = parent.Notes,
                        Company = parent.Company,
                        SubLevelId = parent.SubLevelId,
                        FullAddress = parent.FullAddress,
                    };

                    customerDataList.Add(parentData);
                }
            }
            catch (Exception ex)
            {
                // Log the exception using Serilog (or your logging framework)
                Log.Error(ex, "An error occurred while fetching customers.");
            }

            return customerDataList.OrderBy(c => c.FullName).ToList(); // Order by FullName before returning
        }



        public async Task<List<CustomerData>> GetRecentChangedCustomers(byte[] sinceVersion, int limit = 1200)
        {
            var entities = await _context.Customers
                .FromSqlInterpolated($@"
            SELECT TOP ({limit}) *
            FROM dbo.Customer
            WHERE ChangeVersion > {sinceVersion}
            ORDER BY ChangeVersion")
                .AsNoTracking()
                .ToListAsync();            // async DB call happens here

            var rows = entities
                .Select(c => new CustomerData
                {
                    CustomerID = c.CustomerId,
                    ID = c.Id,
                    ParentID = c.ParentId,
                    Name = c.CustomerName,
                    ParentName = c.ParentName,
                    FullAddress = c.FullAddress,
                    Company = c.Company,
                    FullName = c.FullName,
                    FirstName = c.FirstName,
                    LastName = c.LastName,
                    AccountNumber = c.AccountNumber,
                    Phone = c.Phone,
                    Email = c.Email,
                    Notes = c.Notes,
                    SubLevelId = c.SubLevelId,
                    Description = c.Description,

                    IsActive = c.IsActive ?? false,
                    EffectiveActive = c.EffectiveActive ?? false,
                    MaterializedPath = c.MaterializedPath ?? "",
                    PathIds = c.PathIds ?? "",
                    Depth = c.Depth ?? 0,
                    RootId = c.RootId ?? 0,

                    ServerUpdatedAt = c.ServerUpdatedAt,
                    QBLastUpdated = c.QblastUpdated,
                    ChangeVersion = RowVer.ToHex(c.ChangeVersion)  // safe client-side conversion
                })
                .ToList();

            return rows;
        }



        // Recursive method to get all children of a customer
        private async Task<List<CustomerData>> GetChildren(string parentId)
        {
            // Step 3: Get the children of the given parent
            var children = await _context.Customers
                .Where(c => c.ParentId == parentId)
                .ToListAsync();

            var childDataList = new List<CustomerData>();

            // Step 4: Recursively fetch and add children to the parent
            foreach (var child in children)
            {
                var childData = new CustomerData
                {
                    CustomerID = child.CustomerId,
                    ID = child.Id,
                    ParentID = child.ParentId,
                    ParentName = child.ParentName,
                    Name = child.CustomerName,
                    FullName = child.FullName,
                    FirstName = child.FirstName,
                    LastName = child.LastName,
                    Description = child.Description,
                    AccountNumber = child.AccountNumber,
                    Phone = child.Phone,
                    Email = child.Email,
                    Notes = child.Notes,
                    Company = child.Company,
                    SubLevelId = child.SubLevelId,
                    FullAddress = child.FullAddress,
                    UnitNumber = child.CustomerName,
                    // Recursively fetch this child's children, if any
                    Children = await GetChildren(child.Id)
                };

                childDataList.Add(childData);
            }

            return childDataList;
        }




        public async Task<WatermarkResponse> GetWatermarkAsync(CancellationToken ct = default)
        {
            var maxUtc = await _context.Customers
                .Select(c => (DateTime?)c.LastUpdated)
                .MaxAsync(ct) ?? DateTime.MinValue;

            return new WatermarkResponse { ServerWatermark = maxUtc.ToUniversalTime().ToString("o") };
        }

        public async Task<CustomerChangesResponse> GetCustomerChangesAsync(DateTime? sinceUtc, int limit = 500, CancellationToken ct = default)
        {
            // base: only rows flagged Added/Updated
            var q = _context.Customers
                .AsNoTracking()
                .Where(c => c.UpdateType == "A" || c.UpdateType == "U");

            // optional since filter (strictly greater-than)
            if (sinceUtc is not null)
                q = q.Where(c => c.LastUpdated > sinceUtc.Value);

            // stable ordering for paging
            q = q.OrderBy(c => c.LastUpdated).ThenBy(c => c.Id);

            // materialize page + lookahead to compute hasMore
            var page = await q.Take(limit + 1).Select(c => new CustomerMin
            {
                ID = c.Id,
                ParentID = c.ParentId,
                CustomerName = c.CustomerName,
                ParentName = c.ParentName,
                FullAddress = c.FullAddress,
                LastUpdated = c.LastUpdated
            }).ToListAsync(ct);

            var hasMore = page.Count > limit;
            if (hasMore) page.RemoveAt(page.Count - 1);

            // server-wide watermark (for client cursor)
            var watermark = await _context.Customers
                .AsNoTracking()
                .Select(c => (DateTime?)c.LastUpdated)
                .MaxAsync(ct) ?? DateTime.MinValue;

            return new CustomerChangesResponse
            {
                Items = page,
                Count = page.Count,
                HasMore = hasMore,
                ServerWatermark = watermark.ToUniversalTime().ToString("o")
            };
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

