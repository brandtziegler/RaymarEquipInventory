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
                TechLicences = technician.TechnicianLicences.Select(licence => new TechLicence
                {
                    LicenseID = licence.LicenseId,
                    LicenseName = licence.LicenseName,
                    IssuedDate = licence.IssuedDate,
                    ExpiryDate = licence.ExpiryDate,
                    LicenceUrl = licence.LicenceUrl
                }).ToList() // Handle the 1-many relationship with licenses

            };
            return techDTO;
        }

        public async Task<List<Tech>> GetAllTechs()
        {


            // First, let's get all the technicians, and include the related Person and TechLicences
            var technicians = await _context.Technicians
                .Include(t => t.Person)  // Include the related person
                .Include(t => t.TechnicianLicences)  // Include related licences (1-many)
                .ToListAsync();

            // Map the list of technicians to the DTO
            var techDTOs = technicians.Select(technician => new Tech
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
                TechLicences = technician.TechnicianLicences.Select(licence => new TechLicence
                {
                    LicenseID = licence.LicenseId,
                    LicenseName = licence.LicenseName,
                    IssuedDate = licence.IssuedDate,
                    ExpiryDate = licence.ExpiryDate,
                    LicenceUrl = licence.LicenceUrl
                }).ToList() // Handle the 1-many relationship with licenses
            }).ToList();  // Finally, convert the entire query result into a list of DTOs

            return techDTOs;
        }

        public async Task<List<Tech>> GetTechsByWorkOrder(Int32 workOrderID)
        {

            // Fetch the technicians linked to the work order, including Person and TechnicianLicences
            var technicians = await _context.TechnicianWorkOrders
                .Include(t => t.Technician)  // Include the technician entity
                .ThenInclude(t => t.Person)  // Include the related person
                .Include(t => t.Technician.TechnicianLicences)  // Include related licenses (1-many)
                .Where(w => w.SheetId == workOrderID)
                .Select(w => w.Technician)  // Select the technician from the work order
                .ToListAsync();

            // Map the list of technicians to the DTO
            var techDTOs = technicians.Select(technician => new Tech
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
                TechLicences = technician.TechnicianLicences.Select(licence => new TechLicence
                {
                    LicenseID = licence.LicenseId,
                    LicenseName = licence.LicenseName,
                    IssuedDate = licence.IssuedDate,
                    ExpiryDate = licence.ExpiryDate,
                    LicenceUrl = licence.LicenceUrl
                }).ToList() // Handle the 1-many relationship with licenses
            }).ToList();

            return techDTOs;
        }




    }

}

