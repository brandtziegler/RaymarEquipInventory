namespace RaymarEquipmentInventory.DTOs
{
    public class WorkOrderDetails
    {
        public int SheetId { get; set; }
        public int WorkOrderNumber { get; set; }
        public BillingMin Billing { get; set; }
        public List<PartsUsed> Parts { get; set; } = new List<PartsUsed>();

        public List<Tech> TechWorkOrder { get; set; } = new List<Tech>();
        public List<HourlyLbrSummary> Labour { get; set; } = new List<HourlyLbrSummary>();
        public List<WorkOrderFee> Fees { get; set; } = new List<WorkOrderFee>();
        public List<TravelLog> MileageAndTime { get; set; } = new List<TravelLog>();
        // Add more as needed: signatures, PDFs, etc.
        public List<FeeVisibilityDto> FeeVisibility { get; set; } = new();
    }
}
