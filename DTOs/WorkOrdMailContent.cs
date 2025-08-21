using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    public class WorkOrdMailContent
    {
        public int SheetId { get; set; } = 0;
        public int WorkOrderNumber { get; set; } = 0;
        public string CustPath { get; set; } = "";
        public string WorkDescription { get; set; } = "";
        public string EmailAddress { get; set; } = "";
        public string FirebaseLink { get; set; } = "";

    }

    public sealed class WorkOrdMailContentBatch
    {
        public int SheetId { get; set; }
        public int WorkOrderNumber { get; set; }
        public string WorkOrderFolderId { get; set; } = "";
        public string CustPath { get; set; } = "";
        public string WorkDescription { get; set; } = "";
        public List<string> EmailAddresses { get; set; } = new();
    }
}