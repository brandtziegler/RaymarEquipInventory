namespace RaymarEquipmentInventory.DTOs
{
    public class WorkOrderInsertResult
    {
        public int WorkOrderNumber { get; set; }
        public int SheetId { get; set; }
        public int TechnicianWorkOrderId { get; set; }

        public List<TechnicianWorkOrderMapping> TechnicianMappings { get; set; } = new();
    }
}
