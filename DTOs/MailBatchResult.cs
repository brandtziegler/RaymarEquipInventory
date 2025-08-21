namespace RaymarEquipmentInventory.DTOs
{

    public sealed class MailBatchResult
    {
        public int Attempted { get; set; }
        public int Succeeded { get; set; }
        public List<(string email, string error)> Failed { get; set; } = new();
    }
}
