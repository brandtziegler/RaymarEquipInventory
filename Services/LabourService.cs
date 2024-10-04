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
    public class LabourService : ILabourService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public LabourService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }


        public async Task<List<DTOs.LabourLine>> GetLabourByWorkOrder(int workOrderID)
        {
            var labourList = await _context.Labours
                .Include(t => t.TechnicianWorkOrder)  // Include the related person
                .Include(t => t.TechnicianWorkOrder.Technician)  // Include the related technician
                .Include(t => t.TechnicianWorkOrder.Technician.Person)  // Include the related person
                .Where(t => t.TechnicianWorkOrder.SheetId == workOrderID)
                .ToListAsync();

            var labourDTO = labourList.Select(labour => new DTOs.LabourLine()
            {
                LabourId = labour.LabourId,
                SheetId = labour.TechnicianWorkOrder.SheetId ?? 0,
                StartLabour = labour.StartLabour,
                FinishLabour = labour.FinishLabour,
                DateofLabour = labour.DateOfLabour,
                FlatRateJob = labour.FlatRateJob,
                FlatRateJobDescription = labour.FlatRateJobDescription,
                WorkDescription = labour.WorkDescription,
                TechnicianID = labour.TechnicianWorkOrder?.Technician?.TechnicianId ?? 0,
                TechFirstName = labour.TechnicianWorkOrder?.Technician?.Person?.FirstName ?? "Unknown",
                TechLastName = labour.TechnicianWorkOrder?.Technician?.Person?.LastName ?? "Unknown",
                TechEmail = labour.TechnicianWorkOrder?.Technician?.Person?.Email ?? "No Email",
                TechPhone = labour.TechnicianWorkOrder?.Technician?.Person?.PhoneOne ?? "No Phone"
            }).ToList();


            return labourDTO;

        }

        public async Task<DTOs.LabourLine> GetLabourById(int labourId)
        {
            var labour = await _context.Labours
                .Include(t => t.TechnicianWorkOrder)  // Include the related work order
                .Include(t => t.TechnicianWorkOrder.Technician)  // Include the technician
                 .Include(t => t.TechnicianWorkOrder.Technician.Person)  // Include the related person
                .Where(t => t.LabourId == labourId)
                .FirstOrDefaultAsync();

            if (labour == null)
            {
                return null;  // Return null if no matching labour is found
            }

            var labourDTO = new DTOs.LabourLine()
            {
                LabourId = labour.LabourId,
                SheetId = labour.TechnicianWorkOrder.SheetId ?? 0,
                StartLabour = labour.StartLabour,
                FinishLabour = labour.FinishLabour,
                DateofLabour = labour.DateOfLabour,
                FlatRateJob = labour.FlatRateJob,
                FlatRateJobDescription = labour.FlatRateJobDescription,
                WorkDescription = labour.WorkDescription,
                TechnicianID = labour.TechnicianWorkOrder?.Technician?.TechnicianId ?? 0,
                TechFirstName = labour.TechnicianWorkOrder?.Technician?.Person?.FirstName ?? "Unknown",
                TechLastName = labour.TechnicianWorkOrder?.Technician?.Person?.LastName ?? "Unknown",
                TechEmail = labour.TechnicianWorkOrder?.Technician?.Person?.Email ?? "No Email",
                TechPhone = labour.TechnicianWorkOrder?.Technician?.Person?.PhoneOne ?? "No Phone"
            };

            return labourDTO;
        }



    }

}

