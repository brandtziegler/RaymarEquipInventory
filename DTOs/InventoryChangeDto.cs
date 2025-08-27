namespace RaymarEquipmentInventory.DTOs
{
    public class InventoryChangeDto
    {
        public string InventoryId { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string ManufacturerPartNumber { get; set; } = "";
        public string QuickBooksInvId { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
