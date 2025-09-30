using RaymarEquipmentInventory.DTOs;


namespace RaymarEquipmentInventory.Services
{
    public sealed class QbwcRequestOptions
    {
        public int PageSize { get; set; } = 1500;

        // relax these for now
        public bool ActiveOnly { get; set; } = false;
        public string? FromModifiedDateUtc { get; set; } = null;

        public bool RequestCompanyQueryFirst { get; set; } = true;

        public bool CustomersOnly { get; set; } = false;

    }
}
