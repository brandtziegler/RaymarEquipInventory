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
        private readonly IPermissionsService _permissionsService;

        public TechnicanService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context, IPermissionsService permissionsService)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
            _permissionsService = permissionsService;
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
                    PhoneOne = technician.Person.PhoneOne,
                    RoleName = technician.Person.RoleName
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

        public async Task<SettingsPersonDto> UpsertSettingsPerson(SettingsPersonDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.RoleID <= 0) throw new ArgumentException("RoleID is required.", nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.FirstName)) throw new ArgumentException("FirstName is required.", nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.LastName)) throw new ArgumentException("LastName is required.", nameof(dto));

            // Canonical “Technician” role is 1 in your Roles table
            const int TechnicianRoleId = 1;
            var wantsTechnicianProfile = (dto.RoleID == TechnicianRoleId) || dto.TechTypeId.HasValue || dto.TechnicianID.HasValue;

            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                // ---- 1) PERSON upsert ----
                // NOTE: If your DbSet is _context.Persons (not People), rename here.
                RaymarEquipmentInventory.Models.Person personEntity;

                if (dto.PersonID > 0)
                {
                    personEntity = await _context.People.FirstOrDefaultAsync(p => p.PersonId == dto.PersonID);
                    if (personEntity == null)
                        throw new InvalidOperationException($"PersonID {dto.PersonID} not found.");
                }
                else
                {
                    personEntity = new RaymarEquipmentInventory.Models.Person();
                    _context.People.Add(personEntity);
                }

                // Optional: prevent duplicate emails (ignore empty)
                var normalizedEmail = (dto.Email ?? "").Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(normalizedEmail))
                {
                    var emailExists = await _context.People.AnyAsync(p =>
                        p.PersonId != personEntity.PersonId &&
                        (p.Email ?? "").ToLower() == normalizedEmail
                    );
                    if (emailExists)
                        throw new InvalidOperationException($"Email already exists: {normalizedEmail}");
                }

                personEntity.FirstName = dto.FirstName?.Trim() ?? "";
                personEntity.LastName = dto.LastName?.Trim() ?? "";
                personEntity.Email = normalizedEmail;
                personEntity.PhoneOne = dto.PhoneOne ?? "";
                personEntity.RoleId = dto.RoleID;
                personEntity.RoleName = dto.RoleName ?? "";     // or compute from Roles table later
                if (dto.StartDate.HasValue) personEntity.StartDate = dto.StartDate.Value;
                if (dto.EndDate.HasValue) personEntity.EndDate = dto.EndDate.Value;

                // You added IsActive to Person table + view
                personEntity.IsActive = dto.IsActive;

                await _context.SaveChangesAsync(); // ensures PersonId exists for new rows

                // ---- 2) TECHNICIAN upsert (optional) ----
                Technician techEntity = null;

                if (wantsTechnicianProfile)
                {
                    techEntity = await _context.Technicians
                        .FirstOrDefaultAsync(t => t.PersonId == personEntity.PersonId);

                    if (techEntity == null)
                    {
                        techEntity = new Technician
                        {
                            PersonId = personEntity.PersonId,
                            WorkStatus = "Available",
                            Notes = "",
                            HourlyRate = null
                        };
                        _context.Technicians.Add(techEntity);
                        await _context.SaveChangesAsync(); // ensures TechnicianId exists
                    }

                    // ---- 3) TechnicianAndTypes primary assignment (optional) ----
                    if (dto.TechTypeId.HasValue)
                    {
                        // Try to derive FlatLabourID by matching TechnicianTypes.TypeName to FlatLabours.LabourName
                        var typeName = await _context.TechnicianTypes
                            .Where(tt => tt.TechTypeId == dto.TechTypeId.Value)
                            .Select(tt => tt.TypeName)
                            .FirstOrDefaultAsync();

                        int? flatLabourId = null;
                        if (!string.IsNullOrWhiteSpace(typeName))
                        {
                            var flId = await _context.FlatLabours
                                .Where(fl => fl.LabourName == typeName)
                                .Select(fl => fl.FlatLabourId)
                                .FirstOrDefaultAsync();

                            if (flId > 0) flatLabourId = flId;
                        }

                        // If mapping is missing, don’t silently write junk.
                        if (!flatLabourId.HasValue)
                            throw new InvalidOperationException($"Could not map TechTypeId {dto.TechTypeId.Value} to a FlatLabourID.");

                        var primaryAssign = await _context.TechnicianAndTypes
                            .FirstOrDefaultAsync(a => a.TechnicianId == techEntity.TechnicianId && a.IsPrimary == true);

                        if (primaryAssign == null)
                        {
                            primaryAssign = new TechnicianAndType
                            {
                                TechnicianId = techEntity.TechnicianId,
                                TechTypeId = dto.TechTypeId.Value,
                                FlatLabourId = flatLabourId.Value,
                                RateOverride = null,
                                EffectiveFrom = null,
                                EffectiveTo = null,
                                IsPrimary = true
                            };
                            _context.TechnicianAndTypes.Add(primaryAssign);
                        }
                        else
                        {
                            primaryAssign.TechTypeId = dto.TechTypeId.Value;
                            primaryAssign.FlatLabourId = flatLabourId.Value;
                            primaryAssign.IsPrimary = true;
                        }

                        await _context.SaveChangesAsync();
                    }
                }

                if (!string.IsNullOrWhiteSpace(personEntity.Email))
                {
                    // create AuthUser if missing; don’t reset password on normal edits
                    await _permissionsService.UpsertAuthUserAsync(
                        personEntity.Email,
                        personEntity.PersonId,
                        personEntity.IsActive == true,
                        resetPassword: false
                    );
                }
                await tx.CommitAsync();

                // ---- return refreshed row from the view ----
                var refreshed = await _context.VwPersonWithTechProfiles
                    .AsNoTracking()
                    .Where(x => x.PersonId == personEntity.PersonId)
                    .Select(x => new SettingsPersonDto
                    {
                        PersonID = x.PersonId,
                        FirstName = x.FirstName ?? "",
                        LastName = x.LastName ?? "",
                        Email = x.Email ?? "",
                        RoleName = x.RoleName ?? "",
                        RoleID = x.RoleId ?? 0,
                        PhoneOne = x.PhoneOne ?? "",
                        StartDate = x.StartDate,
                        EndDate = x.EndDate,
                        TechnicianID = x.TechnicianId,
                        TechTypeId = x.TechTypeId,
                        IsActive = x.IsActive
                    })
                    .FirstAsync();

                return refreshed;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                Log.Error(ex, "UpsertSettingsPerson failed for PersonID={PersonID}", dto.PersonID);
                throw;
            }
        }

        public async Task<List<Tech>> GetTechsByWorkOrder(int sheetID)
        {

            // Fetch the technicians linked to the work order, including Person and TechnicianLicences
            var technicians = await _context.TechnicianWorkOrders
                .Include(t => t.Technician)  // Include the technician entity
                .ThenInclude(t => t.Person)  // Include the related person
                .Include(t => t.Technician.TechnicianLicences)  // Include related licenses (1-many)
                .Where(w => w.SheetId == sheetID)
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

        public async Task<List<SettingsPersonDto>> GetSettingsPeople()
        {

           
            return await _context.VwPersonWithTechProfiles
                .AsNoTracking()
                .Select(x => new SettingsPersonDto
                {
                    PersonID = x.PersonId,
                    FirstName = x.FirstName ?? "",
                    LastName = x.LastName ?? "",
                    Email = x.Email ?? "",
                    RoleName = x.RoleName ?? "",
                    RoleID = x.RoleId ?? 0,
                    PhoneOne = x.PhoneOne ?? "",
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    TechnicianID = x.TechnicianId,
                    TechTypeId = x.TechTypeId, 
                    IsActive = x.IsActive
                })
                .ToListAsync();
        }


    }

}

