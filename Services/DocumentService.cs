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
using Azure.Storage.Sas;

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


        public async Task<RetrieveDocument> GetDocumentByID(int docID)
        {
            var documents = new List<RetrieveDocument>();

            var attDoc = await _context.Documents
                .Include(t => t.DocumentType)  // Include the technician entity
                .FirstOrDefaultAsync(w => w.DocumentId == docID);

            if (attDoc == null)
            {
                return null; // Or throw an exception if that's your style
            }

            var docDTO = attDoc.DocumentId != 0 ? new RetrieveDocument
            {
                DocumentID = attDoc.DocumentId,
                SheetID = attDoc.SheetId,
                FileType = attDoc.DocumentType?.DocumentTypeName ?? "",
                FileName = attDoc.FileName,
                UploadDate = attDoc.UploadDate,
                FileURL = attDoc.FileUrl
            } : null;



            return docDTO;

        }

        public async Task<(Stream? Stream, string ContentType, string FileName)> GetDocumentContent(string fileUrl, string fileType)
        {
            try
            {
                // Initialize the BlobServiceClient with the connection string
                var blobServiceClient = new BlobServiceClient(connectionString);
                var blobUri = new Uri(fileUrl);

                // Extract the container name and blob name from the URI
                var containerName = blobUri.Segments[1].TrimEnd('/'); // Container should be the first segment
                var blobName = string.Join("", blobUri.Segments.Skip(2)); // Blob name is the rest

                // Get a reference to the container and then the blob
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                // Check if the blob exists
                if (!await blobClient.ExistsAsync())
                {
                    return (null, string.Empty, string.Empty); // Return if the blob's gone AWOL
                }

                // Generate SAS token with read permissions for the blob
                var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(10));

                // Download the blob using the SAS URI
                var downloadInfo = await new BlobClient(sasUri).DownloadAsync();

                // Set the content type based on file type
                var contentType = fileType.ToLower() switch
                {
                    "pdf" => "application/pdf",
                    "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                    "txt" => "text/plain",
                    "csv" => "text/csv",
                    _ => "application/octet-stream" // Fallback content type
                };

                // Return the file content as a tuple
                return (downloadInfo.Value.Content, contentType, Path.GetFileName(fileUrl));
            }
            catch (Exception ex)
            {
                // Log error like the cynic you are
                Log.Error($"Error downloading document: {ex.Message}");
                return (null, string.Empty, string.Empty); // Return a default tuple if an exception occurs
            }
        }





        public async Task<bool> DocTypeIsValid(string docExtension)
        {
            // Trim and convert both the document type and extension to uppercase for a case-insensitive comparison
            return await _context.DocumentTypes
                .AnyAsync(o => o.DocumentTypeName.ToUpper() == docExtension.Trim().ToUpper());
        }

        public async Task<bool> UploadDoc(IFormFile file, string uploadedBy, int workOrderNumber)
        {
            try
            {
                if (string.IsNullOrEmpty(connectionString))
                {
                    Log.Error("Azure Storage connection string is missing.");
                    return false;
                }

                var sheetID = _context.WorkOrderSheets?.FirstOrDefault(wo => wo.WorkOrderNumber == workOrderNumber)?.SheetId ?? 0;

                if (sheetID == 0)
                {
                    Log.Error($"Work order number {workOrderNumber} not found.");
                    return false;
                }
                var blobServiceClient = new BlobServiceClient(connectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient("workorderdocs");
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

                string blobPath = $"{workOrderNumber}/{file.FileName}";
                var blobClient = containerClient.GetBlobClient(blobPath);

                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, overwrite: true);
                }

                var fileUrl = blobClient.Uri.ToString();

                // Convert current UTC time to the desired local time zone
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                var localUploadDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

                var newDocument = new Document
                {
                    SheetId = sheetID, // Update with actual SheetID if necessary
                    DocumentTypeId = _context.DocumentTypes.FirstOrDefault(dt => dt.DocumentTypeName == Path.GetExtension(file.FileName).TrimStart('.')).DocumentTypeId,
                    FileName = file.FileName,
                    FileUrl = fileUrl,
                    UploadDate = localUploadDate,  // Save as local time
                    UploadedBy = uploadedBy
                };

                _context.Documents.Add(newDocument);
                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error uploading document: {ex.Message}");
                return false;
            }
        }





    }
}

