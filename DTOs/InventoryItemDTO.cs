namespace RaymarEquipmentInventory.DTOs
{
    public sealed class ReceiveParseResult
    {
        public List<InventoryItemDto> InventoryItems { get; set; } = new();
        public List<CustomerData> Customers { get; set; } = new(); // <— add this

        public string? IteratorId { get; set; }
        public int IteratorRemaining { get; set; }
        public int? StatusCode { get; set; }
        public string? StatusMessage { get; set; }
    }
}