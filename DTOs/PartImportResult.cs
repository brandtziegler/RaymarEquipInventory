namespace RaymarEquipmentInventory.DTOs
{
    public class PartImportResult
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Reactivated { get; set; }
        public int MarkedInactive { get; set; }
        public int Rejected { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;



        // Proof samples (up to ~10 per action for audit/debug)
        public List<PartChangeSample> InsertedSamples { get; set; } = new();
        public List<PartChangeSample> UpdatedSamples { get; set; } = new();
        public List<PartChangeSample> ReactivatedSamples { get; set; } = new();
        public List<PartChangeSample> MarkedInactiveSamples { get; set; } = new();
    }
}
