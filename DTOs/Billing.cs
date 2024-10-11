namespace RaymarEquipmentInventory.DTOs
{
    public class Billing
    {
        //adding billing comment.
        public int BillingID { get; set; }
        public string PONo { get; set; } = "";
        public int SheetId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerQBId { get; set; } = "";
        public int ParentCustomerId { get; set; }
        public string ParentCustomerQBId { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ParentCustomerName { get; set; } = "";
        public int? TechId { get; set; }
        public string Notes { get; set; } = "";
        public string UnitNo { get; set; } = "";
        public string WorkLocation { get; set; } = "";


    }
}
