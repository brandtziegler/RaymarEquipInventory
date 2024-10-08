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


        public async Task<List<DTOs.LabourLine>> GetLabourByWorkOrder(int sheetID)
        {
            var labourList = await _context.Labours
                .Include(t => t.TechnicianWorkOrder)  // Include the related person
                .Include(t => t.TechnicianWorkOrder.Technician)  // Include the related technician
                .Include(t => t.TechnicianWorkOrder.Technician.Person)  // Include the related person
                .Where(t => t.TechnicianWorkOrder.SheetId == sheetID)
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

        public async Task<bool> UpdateLabour(LabourLine labourDTO)
        {
            // Step 1: Validate the LabourID
            if (labourDTO.LabourId <= 0)
            {
                Log.Warning("Labour ID is required for updating a labour record.");
                return false;
            }

            try
            {
                // Step 2: Find the existing labour entry by LabourID
                var existingLabour = await _context.Labours.FindAsync(labourDTO.LabourId);

                if (existingLabour == null)
                {
                    Log.Warning($"Labour with ID {labourDTO.LabourId} not found.");
                    return false;
                }

                // Step 3: Retrieve the TechnicianWorkOrderID based on TechnicianID and SheetID
                var technicianWorkOrder = await _context.TechnicianWorkOrders
                    .FirstOrDefaultAsync(t => t.TechnicianId == labourDTO.TechnicianID && t.SheetId == labourDTO.SheetId);

                if (technicianWorkOrder == null)
                {
                    Log.Warning($"TechnicianWorkOrder entry not found for TechnicianID {labourDTO.TechnicianID} and SheetID {labourDTO.SheetId}.");
                    return false;
                }

                // Step 4: Update the fields based on the DTO
                existingLabour.DateOfLabour = labourDTO.DateofLabour ?? existingLabour.DateOfLabour;
                existingLabour.StartLabour = labourDTO.StartLabour ?? existingLabour.StartLabour;
                existingLabour.FinishLabour = labourDTO.FinishLabour ?? existingLabour.FinishLabour;
                existingLabour.FlatRateJob = labourDTO.FlatRateJob;
                existingLabour.FlatRateJobDescription = labourDTO.FlatRateJobDescription ?? existingLabour.FlatRateJobDescription;
                existingLabour.TechnicianWorkOrderId = technicianWorkOrder.TechnicianWorkOrderId; // Map using retrieved ID
                existingLabour.WorkDescription = labourDTO.WorkDescription ?? existingLabour.WorkDescription;

                // Step 5: Save the changes
                _context.Labours.Update(existingLabour);
                await _context.SaveChangesAsync();

                Log.Information($"Successfully updated labour entry with ID {labourDTO.LabourId}.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating labour with ID {labourDTO.LabourId}: {ex.Message}");
                return false;
            }
        }



    }

}

