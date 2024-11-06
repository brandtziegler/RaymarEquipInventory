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

        private string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? "";
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
                FileType = attDoc.DocumentType?.MimeType ?? "",
                FileName = attDoc.FileName,
                UploadDate = attDoc.UploadDate,
                FileURL = attDoc.FileUrl
            } : null;

            return docDTO;
        }

        public async Task<DTOs.InventoryDocument> GetInvDocumentByID(int docID)
        {
            var attDoc = await _context.InventoryDocuments
                .Include(t => t.DocumentType)  // Include the technician entity
                .FirstOrDefaultAsync(w => w.InventoryDocumentId == docID);

            if (attDoc == null)
            {
                return null; // Or throw an exception if that's your style
            }

            var docDTO = attDoc.InventoryDocumentId != 0 ? new DTOs.InventoryDocument
            {
                InventoryDocumentId = attDoc.InventoryDocumentId,
                InventoryId = attDoc.InventoryId,
                DocType = attDoc.DocumentType != null ? new DTOs.DocumentType
                {
                    DocumentTypeId = attDoc.DocumentType.DocumentTypeId,
                    DocumentTypeName = attDoc.DocumentType.DocumentTypeName,
                    MimeType = attDoc.DocumentType.MimeType
                } : new DTOs.DocumentType(), // Ensure DocType is not null
                FileName = attDoc.FileName,
                FileURL = attDoc.FileUrl,
                UploadDate = attDoc.UploadDate,
                UploadedBy = attDoc.UploadedBy
            } : null;

            return docDTO;
        }

        public async Task<DTOs.InventoryDocument> GetPlaceHolderDocument(int docTypeId)
        {
            var attDoc = await _context.PlaceholderDocuments
                .Include(t => t.DocumentType)  // Include the technician entity
                .FirstOrDefaultAsync(w => w.DocumentTypeId == docTypeId);

            if (attDoc == null)
            {
                return null; // Or throw an exception if that's your style
            }

            var docDTO = attDoc != null ? new DTOs.InventoryDocument
            {
                DocType = attDoc.DocumentType != null ? new DTOs.DocumentType
                {
                    DocumentTypeId = attDoc.DocumentType.DocumentTypeId,
                    DocumentTypeName = attDoc.DocumentType.DocumentTypeName,
                    MimeType = attDoc.DocumentType.MimeType
                } : new DTOs.DocumentType(), // Ensure DocType is not null
                FileName = attDoc.FileName,
                FileURL = attDoc.FileUrl,
                UploadDate = attDoc.UploadDate
            } : null;

            return docDTO;
        }


        public async Task<bool> DeleteDocumentById(int docID)
        {
            try
            {
                // Step 1: Retrieve the document details from the database
                var document = await _context.Documents.FindAsync(docID);
                if (document == null)
                {
                    Log.Warning($"Document with ID {docID} not found in the database.");
                    return false; // Document not found
                }

                // Step 2: Retrieve the work order number using the SheetID from the document
                var workOrderSheet = await _context.WorkOrderSheets.FirstOrDefaultAsync(o => o.SheetId == document.SheetId);
                if (workOrderSheet == null)
                {
                    Log.Warning($"WorkOrderSheet with SheetID {document.SheetId} not found for document ID {docID}.");
                    return false; // WorkOrderSheet not found
                }

                var workOrderNumber = workOrderSheet.WorkOrderNumber;

                // Step 3: Initialize the Blob Client for the document's blob
                var blobServiceClient = new BlobServiceClient(connectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient("workorderdocs");
                var blobClient = containerClient.GetBlobClient($"{workOrderNumber}/{document.FileName}"); // Adjust for correct path

                // Step 4: Delete the blob from Azure Storage
                var deleteBlobResponse = await blobClient.DeleteIfExistsAsync();
                if (!deleteBlobResponse.Value)
                {
                    Log.Warning($"Blob for document ID {docID} could not be deleted from storage.");
                    return false; // Could not delete the blob
                }

                // Step 5: Remove the document record from the database
                _context.Documents.Remove(document);
                await _context.SaveChangesAsync();

                Log.Information($"Document with ID {docID} successfully deleted.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error deleting document with ID {docID}: {ex.Message}");
                return false;  // Return false to indicate the operation was unsuccessful
            }
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
                var contentType = fileType.ToLower();

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


        public async Task<bool> UploadPartDocument(IFormFile file, string uploadedBy, int inventoryId)
        {
            try
            {
                if (string.IsNullOrEmpty(connectionString))
                {
                    Log.Error("Azure Storage connection string is missing.");
                    return false;
                }

                // Check if the inventory exists in the database
                var inventoryExists = _context.InventoryData.Any(i => i.InventoryId == inventoryId);
                if (!inventoryExists)
                {
                    Log.Error($"Inventory ID {inventoryId} not found.");
                    return false;
                }

                // Initialize blob service and container client
                var blobServiceClient = new BlobServiceClient(connectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient("partsreceipts");
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

                // Define the folder path based on the inventory ID
                string folderPath = $"{inventoryId}/";

                // List and delete all blobs under the specified folder
                await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: folderPath))
                {
                    var blobToDelete = containerClient.GetBlobClient(blobItem.Name);
                    await blobToDelete.DeleteIfExistsAsync();
                    Log.Information($"Deleted blob: {blobItem.Name}");
                }

                // Define the new blob path and create the blob client
                string blobPath = $"{folderPath}{file.FileName}";
                var blobClient = containerClient.GetBlobClient(blobPath);

                // Upload the file to the cleared folder path
                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, overwrite: true);
                }

                // Get the blob file URL
                var fileUrl = blobClient.Uri.ToString();

                // Convert current UTC time to Eastern Standard Time (local time)
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                var localUploadDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

                // Get Document Type ID based on file extension
                var docTypeId = _context.DocumentTypes
                                        .FirstOrDefault(dt => dt.DocumentTypeName == Path.GetExtension(file.FileName).TrimStart('.'))?.DocumentTypeId;

                // Check if an InventoryDocument entry for this inventoryId already exists
                var existingDocument = _context.InventoryDocuments
                                               .FirstOrDefault(doc => doc.InventoryId == inventoryId);

                if (existingDocument != null)
                {
                    // Update the existing document entry to ensure only one image per inventory item
                    existingDocument.DocumentTypeId = docTypeId ?? 8;
                    existingDocument.FileName = file.FileName;
                    existingDocument.FileUrl = fileUrl;
                    existingDocument.UploadDate = localUploadDate;
                    existingDocument.UploadedBy = uploadedBy;

                    Log.Information($"Updated existing document entry for Inventory ID {inventoryId}.");
                }
                else
                {
                    // Create a new document entry
                    var newInventoryDocument = new RaymarEquipmentInventory.Models.InventoryDocument
                    {
                        InventoryId = inventoryId,
                        DocumentTypeId = docTypeId ?? 8,
                        FileName = file.FileName,
                        FileUrl = fileUrl,
                        UploadDate = localUploadDate,
                        UploadedBy = uploadedBy
                    };

                    _context.InventoryDocuments.Add(newInventoryDocument);
                    Log.Information($"Added new document entry for Inventory ID {inventoryId}.");
                }

                // Save changes to the database
                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error uploading part document: {ex.Message}");
                return false;
            }
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

