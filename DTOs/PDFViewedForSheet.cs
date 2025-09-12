namespace RaymarEquipmentInventory.DTOs
{
    public class PdfViewedForSheet
    {
        public int SheetId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public DateTime ViewedAtUtc { get; set; }
    }
}
