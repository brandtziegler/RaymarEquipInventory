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
using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;

namespace RaymarEquipmentInventory.Services
{
    public class WorkOrderService : IWorkOrderService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public WorkOrderService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }


        public async Task<bool> LaunchWorkOrder(Billing billingInfo)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Step 1: Get the next work order number
                int workOrderNumber = await GetNextWorkOrderNumber();

                // Step 2: Create the WorkOrder record using the generated number
                var newWorkOrder = new WorkOrderSheet
                {
                    WorkOrderNumber = workOrderNumber,
                    DateTimeCreated = DateTime.Now,
                    WorkOrdStatus = "Created",
                    DateTimeCompleted = null,
                    DateTimeStarted = null,
                    // Other initialization steps
                };
                await _context.WorkOrderSheets.AddAsync(newWorkOrder);
                await _context.SaveChangesAsync();


                // Step 3: Create the BillingInformation record and link it to the WorkOrder
                var newBilling = new BillingInformation
                {
                    Pono = billingInfo.PONo,
                    SheetId = newWorkOrder.SheetId, // Link to WorkOrderSheet's SheetID
                    CustomerId = billingInfo.CustomerId,
                    BillingPersonId = billingInfo.TechId,
                    Notes = billingInfo.Notes,
                    UnitNo = billingInfo.UnitNo,
                    WorkLocation = billingInfo.WorkLocation,
                };
                await _context.BillingInformations.AddAsync(newBilling);
                await _context.SaveChangesAsync();


                // Commit transaction
                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Log.Error($"Error launching work order: {ex.Message}");
                return false;
            }
        }

        private async Task<int> GetNextWorkOrderNumber()
        {
            // Fetch the current work order number and increment it
            var workOrderCounter = await _context.WorkOrderCounters.FirstOrDefaultAsync();
            if (workOrderCounter == null)
            {
                throw new InvalidOperationException("WorkOrderCounter record not found.");
            }

            // Increment the work order number
            int newWorkOrderNumber = workOrderCounter.CurrentNumber + 1;
            workOrderCounter.CurrentNumber = newWorkOrderNumber;
            workOrderCounter.LastModified = DateTime.Now;

            // Save the changes to the counter
            _context.WorkOrderCounters.Update(workOrderCounter);
            await _context.SaveChangesAsync();

            return newWorkOrderNumber;
        }
        public async Task<bool> AddTechToWorkOrder(int techID, int sheetID)
        {

            try
            {
                //1. Does the tech exist?
                var tech = await _context.Technicians.Where(t => t.TechnicianId == techID).Include(o => o.Person)
                    .FirstOrDefaultAsync();

                if (tech == null)
                {
                    Log.Error($"Technician does not exist {techID}. ");
                }
                //2. Does the work order exist?
                var workOrder = await _context.WorkOrderSheets.Where(t => t.SheetId == sheetID).FirstOrDefaultAsync();
                if (workOrder == null)
                {
                    Log.Error($"Work Order does not exist {techID}. ");
                }
                //3. Does the tech already exist on the work order?
                var technicianWorkOrder = await _context.TechnicianWorkOrders
                .Where(t => t.SheetId == sheetID && t.TechnicianId == techID).FirstOrDefaultAsync();

                if (technicianWorkOrder != null)
                {
                    Log.Error($"Technician already exists on work order {techID}. ");
                }

                //4. Add the tech to the work order
                var newTechOnWO = new TechnicianWorkOrder()
                {
                    TechnicianId = techID,
                    SheetId = sheetID
                };

                await _context.TechnicianWorkOrders.AddAsync(newTechOnWO);
                await _context.SaveChangesAsync(); // Save all changes asynchronously

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error adding tech to work order");
                return false;
            }

        }

        public async Task<bool> DeleteTechWorkOrderById(int technicianWorkOrderID)
        {
            try
            {
                // Find the TechnicianWorkOrder entry by its ID
                var techWorkOrder = await _context.TechnicianWorkOrders.FindAsync(technicianWorkOrderID);

                if (techWorkOrder == null)
                {
                    // If no entry is found, log a warning and return false
                    Log.Warning($"TechnicianWorkOrder with ID {technicianWorkOrderID} not found.");
                    return false;
                }

                // Remove the entry from the context
                _context.TechnicianWorkOrders.Remove(techWorkOrder);

                // Save the changes to the database
                await _context.SaveChangesAsync();

                Log.Information($"TechnicianWorkOrder with ID {technicianWorkOrderID} successfully deleted.");
                return true;
            }
            catch (Exception ex)
            {
                // Log the exception and return false
                Log.Error($"Error deleting TechnicianWorkOrder with ID {technicianWorkOrderID}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteTechFromWorkOrder(int techID, int sheetID)
        {
            try
            {
                // Find the TechnicianWorkOrder entry using the TechnicianID and SheetID
                var techWorkOrder = await _context.TechnicianWorkOrders.FirstOrDefaultAsync(t => t.TechnicianId == techID && t.SheetId == sheetID);
   
                if (techWorkOrder == null)
                {
                    // If no matching entry is found, log a warning and return false
                    Log.Warning($"Technician with ID {techID} not found for WorkOrder with SheetID {sheetID}.");
                    return false;
                }

                // Remove the entry from the context
                _context.TechnicianWorkOrders.Remove(techWorkOrder);

                // Save the changes to the database
                await _context.SaveChangesAsync();

                Log.Information($"Technician with ID {techID} successfully removed from WorkOrder with SheetID {sheetID}.");
                return true;
            }
            catch (Exception ex)
            {
                // Log the exception and return false
                Log.Error($"Error removing technician with ID {techID} from WorkOrder with SheetID {sheetID}: {ex.Message}");
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
                UnitNo = billing.UnitNo,
                WorkLocation = billing.WorkLocation
            };
            return billingDTO;

        }

        public async Task<bool> RemovePartFromWorkOrder(int partUsedId, int sheetId)
        {
            try
            {
                // Step 1: Find the PartsUsed entity by PartUsedId and SheetId
                var partToRemove = await _context.PartsUseds
                    .FirstOrDefaultAsync(p => p.PartUsedId == partUsedId && p.SheetId == sheetId);

                if (partToRemove == null)
                {
                    // Part not found
                    Log.Warning($"Part with PartUsedId {partUsedId} and SheetId {sheetId} not found.");
                    return false;
                }

                // Step 2: Remove the part from the database
                _context.PartsUseds.Remove(partToRemove);
                await _context.SaveChangesAsync();

                Log.Information($"Part with PartUsedId {partUsedId} successfully removed from Work Order {sheetId}.");
                return true;
            }
            catch (Exception ex)
            {
                // Log error and return failure
                Log.Error($"Error removing part from work order: {ex.Message}");
                return false;
            }
        }


        public async Task<bool> RemoveBillFromWorkOrder(int billingId, int sheetId)
        {
            try
            {
                // Step 1: Find the PartsUsed entity by PartUsedId and SheetId
                var billToRemove = await _context.BillingInformations
                    .FirstOrDefaultAsync(p => p.BillingId == billingId && p.SheetId == sheetId);

                if (billToRemove == null)
                {
                    // Part not found
                    Log.Warning($"Bill with BillikngID {billingId} and SheetId {sheetId} not found.");
                    return false;
                }

                // Step 2: Remove the part from the database
                _context.BillingInformations.Remove(billToRemove);
                await _context.SaveChangesAsync();

                Log.Information($"Bill with Billing ID {billingId} successfully removed from Work Order {sheetId}.");
                return true;
            }
            catch (Exception ex)
            {
                // Log error and return failure
                Log.Error($"Error removing part from work order: {ex.Message}");
                return false;
            }
        }



        public async Task<bool> AddPartToWorkOrder(DTOs.PartsUsed partsUsedDto)
        {
            // Start by checking for required fields in the DTO
            if (partsUsedDto.SheetId == null || partsUsedDto.InventoryId == 0 || partsUsedDto.QtyUsed == null)
            {
                Log.Warning("Failed to add part: SheetID, InventoryId, and QtyUsed are required fields.");
                return false;
            }

            try
            {
                // Step 1: Map the DTO to the PartsUsed entity
                var partUsedEntity = new Models.PartsUsed
                {
                    SheetId = partsUsedDto.SheetId.Value,
                    InventoryId = partsUsedDto.InventoryId,
                    QtyUsed = partsUsedDto.QtyUsed.Value,
                    Notes = partsUsedDto.Notes ?? string.Empty  // Optional, use empty string if null
                };

                // Step 2: Add the part to the database
                await _context.PartsUseds.AddAsync(partUsedEntity);
                await _context.SaveChangesAsync();

                Log.Information($"Successfully added part with InventoryID {partsUsedDto.InventoryId} to Work Order {partsUsedDto.SheetId}");
                return true;
            }
            catch (Exception ex)
            {
                // Log error and return failure
                Log.Error($"Error adding part to work order: {ex.Message}");
                return false;
            }
        }


    }

}

