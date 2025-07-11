﻿using Microsoft.EntityFrameworkCore;
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
    public class MileageAndTravelService : IMileageAndTravelService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public MileageAndTravelService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
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

        public async Task<DTOs.TravelLog> GetTravelByID(int mileageTimeID)
        {
            var travel = await _context.MileageAndTimes.Where(t => t.MilageTimeId == mileageTimeID).FirstOrDefaultAsync();

            if (travel == null)
            {
                return null;  // Return null if no matching labour is found
            }

            var travelDTO = new DTOs.TravelLog()
            {
                SheetId = travel.SheetId,
                DateOfTravel = travel.DateOfMileageTime,
                StartTravel = travel.StartTravel,
                FinishTravel = travel.FinishTravel, 
                StartOdometerKm = travel.StartOdometerKm ?? 0,
                FinishOdometerKm = travel.FinishOdometerKm ?? 0,
                TotalHours = travel.TimeTotalHrs ?? 0,
                TotalMinutes = travel.TimeTotalMin ?? 0,
                SegmentNumber = travel.SegmentNumber,
                TotalDistance = travel.TotalDistance ?? 0,
                TotalOTHours = travel.TotalOthours ?? 0,
                TotalOTMinutes = travel.TotalOtminutes ?? 0,
                IsOvertime = travel.IsOvertime,
            };
            return travelDTO;
        }

        public async Task<bool> DeleteTravelLogAsync(int sheetId)
        {
            try
            {
                if (sheetId <= 0)
                {
                    Log.Warning("SheetID is required for mileage deletion.");
                    return false;
                }

                var entriesToDelete = await _context.MileageAndTimes
                    .Where(m => m.SheetId == sheetId)
                    .ToListAsync();

                if (!entriesToDelete.Any())
                {
                    Log.Information($"🟡 No mileage entries found for SheetID {sheetId}. Nothing to delete.");
                    return true;
                }

                _context.MileageAndTimes.RemoveRange(entriesToDelete);
                await _context.SaveChangesAsync();

                Log.Information($"🗑️ Deleted {entriesToDelete.Count} mileage entries for SheetID {sheetId}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"❌ Failed to delete mileage entries for SheetID {sheetId}: {ex.Message}");
                return false;
            }
        }


        public async Task<bool> InsertTravelLogAsync(TravelLog travel)
        {
            try
            {
                // Step 1: Basic validation
                if (travel.SheetId <= 0)
                {
                    Log.Warning("SheetId is required.");
                    return false;
                }

                if (travel.DateOfTravel == DateTime.MinValue)
                {
                    Log.Warning("DateOfTravel is required and must be valid.");
                    return false;
                }

                // Step 2: Sanitize inputs
                int totalMinutes = travel.TotalMinutes < 0 ? 0 : travel.TotalMinutes;
                int totalHours = travel.TotalHours < 0 ? 0 : travel.TotalHours;
                int otMinutes = travel.TotalOTMinutes < 0 ? 0 : travel.TotalOTMinutes;
                int otHours = travel.TotalOTHours < 0 ? 0 : travel.TotalOTHours;
                int totalDistance = travel.TotalDistance < 0 ? 0 : travel.TotalDistance;

                int startKM = travel.StartOdometerKm < 0 ? 0 : travel.StartOdometerKm;
                int finishKM = travel.FinishOdometerKm < 0 ? 0 : travel.FinishOdometerKm;

                // Step 3: Create entity
                var newEntry = new MileageAndTime
                {
                    SheetId = travel.SheetId,
                    DateOfMileageTime = travel.DateOfTravel,
                    StartTravel = travel.StartTravel,
                    FinishTravel = travel.FinishTravel,
                    StartOdometerKm = startKM,
                    FinishOdometerKm = finishKM,
                    TotalDistance = totalDistance,
                    TimeTotalHrs = totalHours,
                    TimeTotalMin = totalMinutes,
                    TotalOthours = otHours,
                    TotalOtminutes = otMinutes,
                    SegmentNumber = travel.SegmentNumber ?? 1,
                    IsOvertime = travel.IsOvertime
                };

                // Step 4: Save
                await _context.MileageAndTimes.AddAsync(newEntry);
                await _context.SaveChangesAsync();

                Log.Information($"Inserted Travel entry for SheetID {travel.SheetId} on {travel.DateOfTravel:d}");
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? "No inner exception.";
                Log.Error($"❌ Failed to insert Travel entry: {ex.Message} | Inner: {inner}");
                return false;
            }
        }

        public async Task EnsureThreeSegmentsAsync(int sheetId)
        {
            var existingSegments = await _context.MileageAndTimes
                .Where(m => m.SheetId == sheetId)
                .Select(m => m.SegmentNumber)
                .ToListAsync();

            for (int i = 1; i <= 3; i++)
            {
                if (!existingSegments.Contains(i))
                {
                    await _context.MileageAndTimes.AddAsync(new MileageAndTime
                    {
                        SheetId = sheetId,
                        DateOfMileageTime = DateTime.Today,
                        StartTravel = DateTime.Today,
                        FinishTravel = DateTime.Today.AddMinutes(1),
                        SegmentNumber = i,
                        StartOdometerKm = 0,
                        FinishOdometerKm = 0,
                        TotalDistance = 0,
                        TimeTotalHrs = 0,
                        TimeTotalMin = 0,
                        TotalOthours = 0,
                        TotalOtminutes = 0,
                        IsOvertime = false
                    });

                    Log.Information($"🔧 Inserted fallback segment {i} for SheetID {sheetId}");
                }
            }

            await _context.SaveChangesAsync();
        }

    }

}

