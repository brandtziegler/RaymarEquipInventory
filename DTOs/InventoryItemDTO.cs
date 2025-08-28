namespace RaymarEquipmentInventory.DTOs
{
    public sealed class ReceiveParseResult
    {
        public List<InventoryItemDto> InventoryItems { get; set; } = new();
        public string? IteratorId { get; set; }
        public int IteratorRemaining { get; set; }
        public int? StatusCode { get; set; }
        public string? StatusMessage { get; set; }
    }
}
