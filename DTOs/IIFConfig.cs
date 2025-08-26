namespace RaymarEquipmentInventory.DTOs
{
    public record IIFConfig
    {
        //adding billing comment.ssss

        public string ArAccount { get; set; } = "";
        public string LabourItem { get; set; } = "";
        public string LabourOtItem { get; set; } = "";
        public string TravelTimeItem { get; set; } = "";  // was int
        public string MileageItem { get; set; } = "";
        public string MiscPartItem { get; set; } = "";
        public string FeeItem { get; set; } = "";
        public string HstItem { get; set; } = "";
        public decimal HstRate { get; set; } = 0.13m; // was int

    }
}
