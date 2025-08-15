namespace RaymarEquipmentInventory.DTOs
{
    /// <summary>
    /// A single file slated for upload/process, with its chosen container and blob path.
    /// </summary>
    public class PlannedFileInfo
    {
        public PlannedFileInfo() { }

        public PlannedFileInfo(string fileName, string container, string blobPath)
        {
            FileName = fileName;
            Container = container;
            BlobPath = blobPath;
        }

        public string FileName { get; set; } = string.Empty;
        public string Container { get; set; } = string.Empty;
        public string BlobPath { get; set; } = string.Empty;

        public override string ToString() => $"{Container}/{BlobPath} ({FileName})";
    }
}
