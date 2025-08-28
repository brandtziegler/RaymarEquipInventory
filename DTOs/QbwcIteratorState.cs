namespace RaymarEquipmentInventory.DTOs
{
    public sealed class QbwcIteratorState
    {
        public string? IteratorId { get; set; }
        public int Remaining { get; set; }
        public string? LastRequestType { get; set; } // e.g., "ItemInventoryQueryRq"
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
