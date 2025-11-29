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
                existing.HippoNumber = billingDto.HippoNumber?.Trim() ?? existing.HippoNumber;
                existing.CorrigoNumber = billingDto.CorrigoNumber?.Trim() ?? existing.CorrigoNumber;
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


        public async Task<bool> TryInsertBillingInformationAsync(DTOs.Billing dto, CancellationToken ct = default)
        {
            try
            {
                if (dto.SheetId <= 0 || string.IsNullOrWhiteSpace(dto.CustomerQBId))
                {
                    Log.Warning("SheetId and CustomerQBId are required.");
                    return false;
                }

                // Resolve the numeric CustomerId from the stable QuickBooks ID (ListID)
                var cust = await _context.Customers
                    .AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == dto.CustomerQBId, ct); // c.Id = QB ListID column

                if (cust is null)
                {
                    Log.Warning("No customer found for CustomerQBId {CustomerQBId}", dto.CustomerQBId);
                    return false; // or throw, if you truly expect it to always exist
                }

                var resolvedCustomerId = cust.CustomerId; // numeric PK in your Customers table

                // Find existing by SheetId + QB ID (stable), via join to Customers
                var existing = await _context.BillingInformations
                    .Where(b => b.SheetId == dto.SheetId)
                    .Join(_context.Customers,
                          b => b.CustomerId,
                          c => c.CustomerId,
                          (b, c) => new { b, c })
                    .Where(x => x.c.Id == dto.CustomerQBId)
                    .Select(x => x.b)
                    .SingleOrDefaultAsync(ct);

                if (existing != null)
                {
                    // UPDATE path
                    existing.BillingPersonId = dto.BillingPersonID;
                    existing.CustomerId = resolvedCustomerId; // keep numeric in sync
                    existing.Notes = dto.Notes?.Trim() ?? "";
                    existing.UnitNo = dto.UnitNo?.Trim() ?? "";
                    existing.JobSiteCity = dto.JobSiteCity?.Trim() ?? "";
                    existing.Kilometers = dto.Kilometers;
                    existing.Pono = dto.PONo?.Trim() ?? "";
                    existing.HippoNumber = dto.HippoNumber?.Trim() ?? "";
                    existing.CorrigoNumber = dto.CorrigoNumber?.Trim() ?? "";
                    existing.WorkDescription = dto.WorkDescription?.Trim() ?? "";
                    existing.CustPath = dto.CustPath?.Trim() ?? "";

                    await _context.SaveChangesAsync(ct);
                    Log.Information("🟢 Updated BillingInfo for SheetId {SheetId}, CustomerQBId {CustomerQBId}", dto.SheetId, dto.CustomerQBId);
                    return true;
                }

                // INSERT path
                var newEntry = new Models.BillingInformation
                {
                    SheetId = dto.SheetId,
                    BillingPersonId = dto.BillingPersonID,
                    CustomerId = resolvedCustomerId,
                    Notes = dto.Notes?.Trim() ?? "",
                    UnitNo = dto.UnitNo?.Trim() ?? "",
                    JobSiteCity = dto.JobSiteCity?.Trim() ?? "",
                    Kilometers = dto.Kilometers,
                    Pono = dto.PONo?.Trim() ?? "",
                    HippoNumber = dto.HippoNumber?.Trim() ?? "",
                    CorrigoNumber = dto.CorrigoNumber?.Trim() ?? "",
                    WorkDescription = dto.WorkDescription?.Trim() ?? "",
                    CustPath = dto.CustPath?.Trim() ?? ""
                };

                await _context.BillingInformations.AddAsync(newEntry, ct);
                await _context.SaveChangesAsync(ct);

                Log.Information("✅ Inserted BillingInfo for SheetId {SheetId}, CustomerQBId {CustomerQBId}", dto.SheetId, dto.CustomerQBId);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("❌ Failed to upsert BillingInfo: {Message} | Inner: {Inner}",
                    ex.Message, ex.InnerException?.Message ?? "No inner exception");
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
              .Include(t => t.Customer.ParentCustomer)  // Include the related work order
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
                ParentCustomerId = billing.Customer.ParentCustomer.CustomerId,
                ParentCustomerName = billing.Customer.ParentCustomer.CustomerName,
                CustomerQBId = billing.Customer.Id,
                ParentCustomerQBId = billing.Customer.ParentCustomer.Id,
                PONo = billing.Pono,
                HippoNumber = billing.HippoNumber,
                CorrigoNumber = billing.CorrigoNumber,
                Notes = billing.Notes,
                UnitNo = billing.UnitNo
            };
            return billingDTO;

        }
    }

}

