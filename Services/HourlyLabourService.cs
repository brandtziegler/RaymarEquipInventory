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




    }

}

