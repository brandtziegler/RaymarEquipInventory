namespace RaymarEquipmentInventory.DTOs
{
    public class InventoryData
    {
        public string InventoryId { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string ManufacturerPartNumber { get; set; } = "";
        public string QuickBooksInvId { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal? Cost { get; set; }
        public decimal? SalesPrice { get; set; }
        public int? ReorderPoint { get; set; }
        public int? OnHand { get; set; }
        public decimal? AverageCost { get; set; }
        public int? IncomeAccountId { get; set; }

        public List<RaymarEquipmentInventory.DTOs.InventoryDocument> InventoryDocuments { get; set; } = new List<RaymarEquipmentInventory.DTOs.InventoryDocument>();
        public DateTime? CreatedDate { get; set; } = default(DateTime?);

        public DateTime? UpdatedDate { get;set; }   
    }
}
