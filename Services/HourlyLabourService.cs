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
using Microsoft.AspNetCore.Server.IISIntegration;

namespace RaymarEquipmentInventory.Services
{
    public class HourlyLabourService : IHourlyLabourService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public HourlyLabourService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }


        public async Task<List<DTOs.HourlyLabourType>> GetAllHourlyLabourTypes()
        {
            var labourList = await _context.HourlyLabourTypes.ToListAsync();

            var labourDTO = labourList.Select(labour => new DTOs.HourlyLabourType()
            {
                LabourTypeID = labour.LabourTypeId,
                LabourTypeDescription = labour.LabourTypeDescription,
                IsDefault = labour.IsDefault,
            }).ToList();


            return labourDTO;

        }

        public async Task<DTOs.HourlyLabourType> GetHourlyLabourById(int hourlyLabourId)
        {
            var labour = await _context.HourlyLabourTypes.Where(t => t.LabourTypeId == hourlyLabourId).FirstOrDefaultAsync();

            if (labour == null)
            {
                return null;  // Return null if no matching labour is found
            }

            var labourDTO = new DTOs.HourlyLabourType()
            {
                LabourTypeID = labour.LabourTypeId,
                LabourTypeDescription = labour.LabourTypeDescription,
                IsDefault = labour.IsDefault,
            };

            return labourDTO;
        }

        public async Task<bool> InsertRegularLabourAsync(RegularLabourLine labour)
        {
            try
            {
                // Step 1: Basic validation
                if (labour.TechnicianWorkOrderID <= 0)
                {
                    Log.Warning("TechnicianWorkOrderID is required.");
                    return false;
                }

                if (labour.LabourTypeID <= 0)
                {
                    Log.Warning("LabourTypeID is required.");
                    return false;
                }

                if (labour.DateOfLabor == DateTime.MinValue)
                {
                    Log.Warning("DateOfLabor is required and must be valid.");
                    return false;
                }

                // Step 2: Sanitize/normalize values
                int totalHours = labour.TotalHours < 0 ? 0 : labour.TotalHours;
                int totalMinutes = labour.TotalMinutes < 0 ? 0 : labour.TotalMinutes;
                int otHours = labour.TotalOTHours < 0 ? 0 : labour.TotalOTHours;
                int otMinutes = labour.TotalOTMinutes < 0 ? 0 : labour.TotalOTMinutes;
                string workDescription = labour.WorkDescription?.Trim() ?? "";

                // Step 3: Construct the entity
                var newEntry = new RegularLabour
                {
                    TechnicianWorkOrderId = labour.TechnicianWorkOrderID,
                    DateOfLabor = labour.DateOfLabor,
                    StartLabor = labour.StartLabor,
                    FinishLabor = labour.FinishLabor,
                    WorkDescription = workDescription,
                    TotalHours = totalHours,
                    TotalMinutes = totalMinutes,
                    TotalOthours = otHours,
                    TotalOtminutes = otMinutes,
                    LabourTypeId = labour.LabourTypeID
                };

                // Step 4: Add + save
                await _context.RegularLabours.AddAsync(newEntry);
                await _context.SaveChangesAsync();

                Log.Information($"Inserted RegularLabour entry for TechW/O ID {labour.TechnicianWorkOrderID} on {labour.DateOfLabor:d}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to insert RegularLabour entry: {ex.Message}");
                return false;
            }
        }





    }

}

