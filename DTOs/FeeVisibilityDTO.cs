namespace RaymarEquipmentInventory.DTOs
{
    public class FeeVisibilityDto
    {
        public int TechnicianWorkOrderID { get; set; }   // will be normalized to query id
        public int LabourTypeID { get; set; }
        public int IsVisible { get; set; }               // 0/1 from device
    }


}
