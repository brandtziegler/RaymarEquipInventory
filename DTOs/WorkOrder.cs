using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    public class WorkOrder
    {

        public Billing WOBilling { get; set; } = new Billing();
        public List<LabourLine> LabourLines { get; set; } = new List<LabourLine>();
        public List<PartsUsed> PartsUsed { get; set; } = new List<PartsUsed>();
        public List<TripLog> VehicleTravelLogs { get; set; } = new List<TripLog>();
        public List<Technician> Technicians { get; set; } = new List<Technician>();

        public Customer Customer { get; set; } = new Customer();
        public Customer ParentCustomer { get; set; } = new Customer();
        public string UnitNo { get; set; } = "";
        public string WorkLocation { get; set; } = "";

        public int SheetId { get; set; }
        public int WorkOrderNumber { get; set; }

        public string WorkOrderStatus { get; set; } = "";
        public DateTime? DateTimeCreated { get; set; }
        public DateTime? DateTimeCompleted { get; set; }
        public DateTime? DateTimeStarted { get; set; }

    }
}
