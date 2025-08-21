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

        public async Task<bool> UpdateBillingInformationAsync(
       DTOs.Billing billingDto, CancellationToken ct = default)
        {
            try
            {
                var existing = await _context.BillingInformations
                    .FirstOrDefaultAsync(b => b.SheetId == billingDto.SheetId, ct);
                if (existing == null)
                {
                    Log.Warning("⚠️ No billing record found for SheetID {SheetId}", billingDto.SheetId);
                    return false;
                }

                existing.BillingPersonId = billingDto.BillingPersonID;
                existing.CustomerId = billingDto.CustomerId;
                existing.Notes = billingDto.Notes?.Trim() ?? existing.Notes;
                existing.UnitNo = billingDto.UnitNo?.Trim() ?? existing.UnitNo;
                existing.JobSiteCity = billingDto.JobSiteCity?.Trim() ?? existing.JobSiteCity;
                existing.Kilometers = billingDto.Kilometers;
                existing.Pono = billingDto.PONo?.Trim() ?? existing.Pono;
                existing.WorkDescription = billingDto.WorkDescription?.Trim() ?? existing.WorkDescription;
                existing.CustPath = billingDto.CustPath?.Trim() ?? existing.CustPath;

                await _context.SaveChangesAsync(ct);
                Log.Information("✅ Updated BillingInfo for SheetID {SheetId}", billingDto.SheetId);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Failed to update BillingInfo for SheetID {SheetId}", billingDto.SheetId);
                return false;
            }
        }


        public async Task<bool> TryInsertBillingInformationAsync(DTOs.Billing billingDto, CancellationToken cancellationToken = default)
        {
            try
            {
                // Step 1: Basic validation
                if (billingDto.SheetId <= 0 || billingDto.CustomerId <= 0)
                {
                    Log.Warning("SheetId and CustomerId are required.");
                    return false;
                }

                // Step 2: Check if entry already exists
                bool exists = await _context.BillingInformations
                    .AnyAsync(b => b.SheetId == billingDto.SheetId);

                if (exists)
                {
                    Log.Warning($"🟡 Billing entry already exists for SheetID {billingDto.SheetId}. Skipping insert.");
                    return true; // Not a failure — just not an insert
                }

                // Step 3: Create new entity
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

                // Step 4: Save to DB
                await _context.BillingInformations.AddAsync(newEntry);
                await _context.SaveChangesAsync();

                Log.Information($"✅ Inserted BillingInfo for SheetID {billingDto.SheetId} and CustomerID {billingDto.CustomerId}");
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? "No inner exception";
                Log.Error($"❌ Failed to insert BillingInfo: {ex.Message} | Inner: {inner}");
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

