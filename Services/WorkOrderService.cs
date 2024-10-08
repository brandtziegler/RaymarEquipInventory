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


        public async Task<bool> LaunchWorkOrder(BillingInformation billingInfo)
        {


            return true;

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
    }

}

