namespace RaymarEquipmentInventory.DTOs
{
    public sealed class InventoryBackupSyncResult
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Deactivated { get; set; }
    }

}
