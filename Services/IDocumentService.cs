using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IDocumentService
    {
        Task<List<RetrieveDocument>> GetDocumentsByWorkOrder(int sheetID);
        Task<bool> DocTypeIsValid(string docExtension);
        //Task<RetrieveDocument> GetDocumentById(int documentID);
        Task<bool> UploadDoc(UploadDocument documentDTO);  // For uploading a document
        //Task<bool> DeleteDocument(int documentID);
        //Task<bool> DeleteDocuments(int sheetID);
    }

}
