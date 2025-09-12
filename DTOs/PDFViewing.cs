namespace RaymarEquipmentInventory.DTOs
{
    public class ReplaceViewedForSheetRequest
    {
        public List<PdfViewedItem> ViewedList { get; set; } = new();
    }
    public class PdfViewedItem
    {
        public string FileName { get; set; } = string.Empty;
        public DateTime ViewedAtUtc { get; set; }  // ISO-8601 from device
    }
}
