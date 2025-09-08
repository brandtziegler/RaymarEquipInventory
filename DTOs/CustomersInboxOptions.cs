namespace RaymarEquipmentInventory.DTOs
{
    public class CustomersInboxOptions
    {
        public string ImapHost { get; set; } = default!;
        public int ImapPort { get; set; } = 993; // SSL
        public string Email { get; set; } = default!;
        public string Password { get; set; } = default!;
        public string ExpectedSubject { get; set; } = "Customer Upsert";
        public string ExpectedAttachment { get; set; } = "CustomerUpsert";
    }
}
