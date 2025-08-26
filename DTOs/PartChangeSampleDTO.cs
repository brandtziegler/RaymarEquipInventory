namespace RaymarEquipmentInventory.DTOs
{
    public class PartChangeSample
    {
        public string Key { get; set; } = string.Empty;          // ManufacturerPartNumber

        public string Action { get; set; } = string.Empty;       // Inserted | Updated | Reactivated | MarkedInactive

        // Before state
        public string? BeforeItemName { get; set; }
        public decimal? BeforePrice { get; set; }
        public bool? BeforeIsActive { get; set; }

        // After state
        public string? AfterItemName { get; set; }
        public decimal? AfterPrice { get; set; }
        public bool? AfterIsActive { get; set; }
    }
}