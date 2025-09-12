namespace RaymarEquipmentInventory.DTOs
{
    public sealed class PartsInboxOptions
    {
        public string ImapHost { get; set; } = default!;
        public int ImapPort { get; set; }
        public string Email { get; set; } = default!;
        public string Password { get; set; } = default!;
        public string ExpectedSubject { get; set; } = "Inventory Upsert";
        public string ExpectedAttachment { get; set; } = "InventoryUpsert";
    }
}
