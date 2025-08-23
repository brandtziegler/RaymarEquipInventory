namespace RaymarEquipmentInventory.DTOs
{
    /// <summary>
    /// Maps 1:1 to vw_InvoicePreviewSummed columns for CSV export.
    /// </summary>
    public class InvoiceCSVRow
    {
        public string Category { get; set; } = "";  // PartsUsed, MileageAndTravel, RegularLabour, WorkOrderFees
        public int SheetID { get; set; }
        public int? TechnicianID { get; set; }
        public string? TechnicianName { get; set; }
        public string ItemName { get; set; } = "";
        public decimal UnitPrice { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
