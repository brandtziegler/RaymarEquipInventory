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
    public class BillingService : IBillingService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public BillingService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }

        public async Task<bool> AddCustomerToBill(int billId, int customerId, string unitNo)
        {
            try
            {
                // Retrieve the bill from the database
                var bill = await _context.BillingInformations.Include(b => b.Customer)
                                                .FirstOrDefaultAsync(b => b.BillingId == billId);
                if (bill == null)
                {
                    Log.Warning($"Bill with ID {billId} not found.");
                    return false;
                }

                // Retrieve the customer
                var customer = await _context.Customers.FindAsync(customerId);
                if (customer == null)
                {
                    Log.Warning($"Customer with ID {customerId} not found.");
                    return false;
                }

                // Associate the customer with the bill
                bill.Customer = customer;
                bill.UnitNo = unitNo;
                await _context.SaveChangesAsync();

                Log.Information($"Customer {customerId} successfully added to Bill {billId}.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error adding Customer {customerId} to Bill {billId}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> InsertBillingInformationAsync(DTOs.Billing billingDto)
        {
            try
            {
                // Step 1: Basic validation
                if (billingDto.SheetId <= 0 || billingDto.CustomerId <= 0)
                {
                    Log.Warning("SheetId and CustomerId are required.");
                    return false;
                }

                // Step 2: Create entity
                var newEntry = new Models.BillingInformation
                {
                    SheetId = billingDto.SheetId,
                    BillingPersonId = billingDto.BillingPersonID,
                    CustomerId = billingDto.CustomerId,
                    Notes = billingDto.Notes?.Trim() ?? "",
                    UnitNo = billingDto.UnitNo?.Trim() ?? "",
                    JobSiteCity = billingDto.JobSiteCity?.Trim() ?? "",
                    Kilometers = billingDto.Kilometers,
                    Pono = billingDto.PONo?.Trim() ?? "",
                    WorkDescription = billingDto.WorkDescription?.Trim() ?? "",
                    CustPath = billingDto.CustPath?.Trim() ?? ""
                };

                // Step 3: Save to DB
                await _context.BillingInformations.AddAsync(newEntry);
                await _context.SaveChangesAsync();

                Log.Information($"✅ Inserted BillingInfo for SheetID {billingDto.SheetId} and CustomerID {billingDto.CustomerId}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"❌ Failed to insert BillingInfo: {ex.Message}");
                return false;
            }
        }


        public async Task<bool> UpdateBillingInfo(Billing billingDto)
        {
            try
            {
                // Validation for required fields
                if (billingDto.CustomerId == 0 || string.IsNullOrWhiteSpace(billingDto.Notes))
                {
                    Log.Warning("CustomerId and Notes are mandatory fields and must be provided.");
                    return false; // Don’t proceed if these fields are missing
                }

                // Step 1: Find the existing record in the database
                var billingRecord = await _context.BillingInformations
                    .FirstOrDefaultAsync(b => b.BillingId == billingDto.BillingID);

                if (billingRecord == null)
                {
                    Log.Warning($"Billing record with ID {billingDto.BillingID} not found.");
                    return false;
                }

                // Step 2: Update fields with data from the DTO
                billingRecord.Pono = billingDto.PONo;
                billingRecord.SheetId = billingDto.SheetId;
                billingRecord.CustomerId = billingDto.CustomerId;
                billingRecord.Notes = billingDto.Notes;
                billingRecord.UnitNo = billingDto.UnitNo;
                billingRecord.WorkLocation = billingDto.JobSiteCity;
                // Add any additional fields you want to update

                // Step 3: Save changes back to the database
                await _context.SaveChangesAsync();

                Log.Information($"Billing record with ID {billingDto.BillingID} updated successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating billing record with ID {billingDto.BillingID}: {ex.Message}");
                return false;
            }
        }


        public async Task<bool> RemoveCustomerFromBill(int billId, int customerId)
        {
            try
            {
                // Retrieve the bill
                var bill = await _context.BillingInformations.Include(b => b.Customer)
                                                .FirstOrDefaultAsync(b => b.BillingId == billId);
                if (bill == null)
                {
                    Log.Warning($"Bill with ID {billId} not found.");
                    return false;
                }

                var customer = bill.Customer;
                //// Find the customer in the bill's customer list
                //var customer = bill.Customer.FirstOrDefault(c => c.CustomerId == customerId);
                if (customer == null)
                {
                    Log.Warning($"Customer {customerId} not associated with Bill {billId}.");
                    return false;
                }

                // Remove the association
                bill.Customer = null;
                await _context.SaveChangesAsync();

                Log.Information($"Customer {customerId} successfully removed from Bill {billId}.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error removing Customer {customerId} from Bill {billId}: {ex.Message}");
                return false;
            }
        }

        public async Task<DTOs.Billing> GetLabourForWorkorder(int sheetID)
        {
            var billing = await _context.BillingInformations
              .Include(t => t.Customer)  // Include the related work order
              .Include(t => t.Customer.Parent)  // Include the related work order
              .Where(t => t.SheetId == sheetID)
              .FirstOrDefaultAsync();

            if (billing == null)
            {
                return null;  // Return null if no matching labour is found
            }

            var billingDTO = new DTOs.Billing()
            {
                BillingID = billing.BillingId,
                SheetId = billing.SheetId,
                CustomerId = billing.Customer.CustomerId,
                CustomerName = billing.Customer.CustomerName,
                ParentCustomerId = billing.Customer.Parent.CustomerId,
                ParentCustomerName = billing.Customer.Parent.CustomerName,
                CustomerQBId = billing.Customer.Id,
                ParentCustomerQBId = billing.Customer.Parent.Id,
                PONo = billing.Pono,
                Notes = billing.Notes,
                UnitNo = billing.UnitNo
            };
            return billingDTO;

        }
    }

}

