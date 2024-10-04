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
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.IO;

namespace RaymarEquipmentInventory.Services
{
    public class DocumentService : IDocumentService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;

        private string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
        public DocumentService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }


        public async Task<List<RetrieveDocument>> GetDocumentsByWorkOrder(int sheetID)
        {
            var documents = new List<RetrieveDocument>();

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

        public async Task<bool> UploadDoc(UploadDocument documentDTO, string workOrderNumber)
        {
            try
            {
                // Ensure connection string is set
                if (string.IsNullOrEmpty(connectionString))
                {
                    Log.Error("Azure Storage connection string is missing.");
                    return false;
                }

                // Create BlobServiceClient to interact with Blob storage
                var blobServiceClient = new BlobServiceClient(connectionString);

                // Get the container client (make sure to create it first in Azure)
                var containerClient = blobServiceClient.GetBlobContainerClient("workorderdocs");

                // Ensure the container exists (create it if not already there)
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

                // Create the subfolder path using workOrderNumber
                string folderPath = $"{workOrderNumber}/{documentDTO.FileName}";

                // Get a reference to the Blob (file) in Azure Blob storage
                var blobClient = containerClient.GetBlobClient(folderPath);

                // Convert file content into a stream (assuming it's a byte array in the DTO for now)
                byte[] fileContent = Convert.FromBase64String(documentDTO.FileURL);  // This could be adjusted based on how you're handling the file
                using (var stream = new MemoryStream(fileContent))
                {
                    // Upload the file to Azure Blob Storage
                    await blobClient.UploadAsync(stream, overwrite: true);
                }

                // Update the FileURL in the documentDTO with the Azure Blob URL
                documentDTO.FileURL = blobClient.Uri.ToString();

                // Assuming you need to store the uploaded file's information in your database
                var newDocument = new Document
                {
                    SheetId = documentDTO.SheetID,
                    DocumentTypeId = _context.DocumentTypes.FirstOrDefault(dt => dt.DocumentTypeName == documentDTO.FileType).DocumentTypeId,
                    FileName = documentDTO.FileName,
                    FileUrl = documentDTO.FileURL,
                    UploadDate = DateTime.UtcNow
                };

                _context.Documents.Add(newDocument);
                await _context.SaveChangesAsync();

                return true;  // Return success
            }
            catch (Exception ex)
            {
                Log.Error($"Error uploading document: {ex.Message}");
                return false;  // Handle or log any exceptions that occur
            }
        }



    }
}

