namespace RaymarEquipmentInventory.DTOs
{
    public sealed class CustomerCsvRow
    {
        public string FullName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Company { get; set; } = "";
        public string FullAddress { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public bool IsActive { get; set; }

        // Derived
        public string? ParentFullName { get; set; }
        public int Depth { get; set; }
    }
}
