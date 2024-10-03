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
    public class TechnicanService : ITechnicianService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public TechnicanService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }




        public async Task<Tech> GetTechByID(int techID)
        {
            // First, let's get the tech, and include the related Person and TechLicences
            var technician = await _context.Technicians
                .Include(t => t.Person)  // Include the related person
                .Include(t => t.TechnicianLicences)  // Include related licences (1-many)
                .FirstOrDefaultAsync(t => t.TechnicianId == techID);

            if (technician == null)
            {
                return null; // Or throw an exception if that's your style
            }

            // We map it to the DTO you showed in the image
            var techDTO = new Tech
            {
                TechnicianID = technician.TechnicianId,
                Notes = technician.Notes,
                HourlyRate = technician.HourlyRate ?? 0,
                Person = new DTOs.Person
                {
                    FirstName = technician.Person.FirstName,
                    LastName = technician.Person.LastName,
                    Email = technician.Person.Email,
                    PhoneOne = technician.Person.PhoneOne
                },
                TechLicences = new List<TechLicence>()

            };


            // Fetch the licences for this technician
            var licences = await _context.TechnicianLicences
                .Where(l => l.TechnicianId == techID)
                .ToListAsync();

            // Now map those licenses to the DTO
            techDTO.TechLicences = licences.Select(licence => new TechLicence
            {
                LicenseID = licence.LicenseId,
                LicenseName = licence.LicenseName,
                IssuedDate = licence.IssuedDate,
                ExpiryDate = licence.ExpiryDate,
                LicenceUrl = licence.LicenceUrl
            }).ToList();



            return techDTO;
        }





    }

}

