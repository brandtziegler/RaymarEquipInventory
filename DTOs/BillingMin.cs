namespace RaymarEquipmentInventory.DTOs
{
    public class BillingMin
    {
        public int SheetId { get; set; }
        public int CustomerId { get; set; }

        public string PONo { get; set; } = string.Empty;
        public string HippoNumber { get; set; } = string.Empty;
        public string CorrigoNumber { get; set; } = string.Empty;
        public int Kilometers { get; set; } = 0;

        public string UnitNo { get; set; } = string.Empty;
        public string JobSiteCity { get; set; } = string.Empty;
        public string WorkDescription { get; set; } = string.Empty;
		public string ThirdPartyContractor { get; set; } = string.Empty;

		public string CustPath { get; set; } = string.Empty;

        public string WorkOrderStatus { get; set; } = string.Empty;
        public DateTime? DateUploaded { get; set; }
        public int? CompletedBy { get; set; }
    }

}
