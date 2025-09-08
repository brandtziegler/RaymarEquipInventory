namespace RaymarEquipmentInventory.DTOs
{
    public sealed class CustomerImportResult
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Reparented { get; set; }
        public int Rejected { get; set; }
    }
}
