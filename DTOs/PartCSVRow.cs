namespace RaymarEquipmentInventory.DTOs
{
    public class PartCSVRow
    {
        public string Item { get; set; } = string.Empty;           // Maps to ManufacturerPartNumber
        public string Description { get; set; } = string.Empty;    // Maps to ItemName + Description
        public string Uom { get; set; } = string.Empty;            // Unit of Measure
        public decimal Price { get; set; }                        // Maps to SalesPrice
    }
}
