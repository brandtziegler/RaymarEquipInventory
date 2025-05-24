namespace RaymarEquipmentInventory.DTOs
{
    public class Billing
    {
        //adding billing comment.ssss
        public int BillingID { get; set; } = 0;
        public int SheetId { get; set; } = 0;
        public int BillingPersonID { get; set; } = 0;
        public int CustomerId { get; set; } = 0;
        public string Notes { get; set; } = "";
        public string UnitNo { get; set; } = "";
        public string JobSiteCity { get; set; } = "";
        public int Kilometers { get; set; } = 0;
        public string PONo { get; set; } = "";
        public string WorkDescription { get; set; } = "";
        public string CustPath { get; set; } = "";
        public string CustomerQBId { get; set; } = "";
        public int ParentCustomerId { get; set; }
        public string ParentCustomerQBId { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ParentCustomerName { get; set; } = "";
        public int? TechId { get; set; }


    }
}
