using RaymarEquipmentInventory.DTOs;


namespace RaymarEquipmentInventory.Services
{
    public sealed class QbwcRequestOptions
    {
        public int PageSize { get; set; } = 100;
        public bool ActiveOnly { get; set; } = true;
        public string? FromModifiedDateUtc { get; set; } // e.g., "2025-08-01T00:00:00"
        public bool RequestCompanyQueryFirst { get; set; } = true;
    }
}
