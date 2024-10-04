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

