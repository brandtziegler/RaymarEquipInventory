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
    public class DocumentService : IDocumentService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public DocumentService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }


        public async Task<List<RetrieveDocument>> GetDocumentsByWorkOrder(int sheetID)
        {
            var documents  = new List<RetrieveDocument>();

            // First, let's get the tech, and include the related Person and TechLicences
            var attachedDocs = await _context.Documents
                .Include(t => t.DocumentType)  // Include the technician entity
                .Where(w => w.SheetId == sheetID).ToListAsync();

            if (attachedDocs.Any() == false)
            {
                return null; // Or throw an exception if that's your style
            }

            var documentDTOs = attachedDocs.Select(doc => new RetrieveDocument
            {
                DocumentID = doc.DocumentId,
                SheetID = doc.SheetId,
                FileType = doc.DocumentType?.DocumentTypeName ?? "",
                FileName = doc.FileName,
                UploadDate = doc.UploadDate,
                FileURL = doc.FileUrl
            }).ToList();
           

            return documentDTOs;

        }

        public async Task<bool> DocTypeIsValid(string docExtension)
        {
            // Trim and convert both the document type and extension to uppercase for a case-insensitive comparison
            return await _context.DocumentTypes
                .AnyAsync(o => o.DocumentTypeName.ToUpper() == docExtension.Trim().ToUpper());
        }


    }

}

