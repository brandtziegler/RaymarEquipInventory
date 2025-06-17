namespace RaymarEquipmentInventory.DTOs
{
    public class WorkOrderDetails
    {
        public int SheetId { get; set; }
        public List<Tech> TechWorkOrder = new List<Tech>();
        public BillingMin Billing { get; set; }
        public List<PartsUsed> Parts { get; set; } = new List<PartsUsed>(); 
        public List<RegularLabourLine> Labour { get; set; } = new List<RegularLabourLine>();
        public List<WorkOrderFee> Fees { get; set; } = new List<WorkOrderFee>();
        public List<TravelLog> MileageAndTime { get; set; } = new List<TravelLog>();
        // Add more as needed: signatures, PDFs, etc.
    }
}
