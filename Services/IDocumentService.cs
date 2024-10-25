using RaymarEquipmentInventory.DTOs;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public interface IDocumentService
    {
        Task<List<RetrieveDocument>> GetDocumentsByWorkOrder(int sheetID);
        Task<bool> DocTypeIsValid(string docExtension);
        //Task<RetrieveDocument> GetDocumentById(int documentID);
        Task<RetrieveDocument> GetDocumentByID(int docID);

        Task<bool> DeleteDocumentById(int docID);

        Task<(Stream? Stream, string ContentType, string FileName)> GetDocumentContent(string fileUrl, string fileType);

        Task<bool> UploadDoc(IFormFile file, string uploadedBy, int workOrderNumber);  // For uploading a document
        //Task<bool> DeleteDocument(int documentID);
        //Task<bool> DeleteDocuments(int sheetID);

        Task<bool> UploadPartDocument(IFormFile file, string uploadedBy, int inventoryId);


    }

}
