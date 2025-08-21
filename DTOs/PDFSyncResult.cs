namespace RaymarEquipmentInventory.DTOs
{
    public class PDFSyncResult
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }

        public string Trashed { get; set; } = string.Empty;

        public DateTimeOffset RanAtUtc { get; set; }

    }
}
