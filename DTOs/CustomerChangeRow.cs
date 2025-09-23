namespace RaymarEquipmentInventory.DTOs
{
    public class CustomerChangeRow
    {
        public int CustomerID { get; set; }
        public string ID { get; set; } = "";
        public string ParentID { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ParentName { get; set; } = "";
        public string FullAddress { get; set; } = "";
        public string MaterializedPath { get; set; } = "";
        public string PathIds { get; set; } = "";
        public int Depth { get; set; }
        public int RootId { get; set; }
        public bool IsActive { get; set; }
        public bool EffectiveActive { get; set; }
        public DateTime ServerUpdatedAt { get; set; }
        public DateTime? QBLastUpdated { get; set; }
        public byte[] ChangeVersion { get; set; } = Array.Empty<byte>();
    }
}
