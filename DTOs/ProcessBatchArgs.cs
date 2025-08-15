namespace RaymarEquipmentInventory.DTOs
{
    /// <summary>
    /// A single file slated for upload/process, with its chosen container and blob path.
    /// </summary>
    /// <summary>
    /// Arguments passed to background jobs for a single upload batch.
    /// </summary>
    public class ProcessBatchArgs
    {
        public ProcessBatchArgs() { }

        public ProcessBatchArgs(
            string workOrderId,
            string batchId,
            string workOrderFolderId,
            string imagesFolderId,
            string pdfFolderId,
            string expensesFolderId,
            List<PlannedFileInfo> files)
        {
            WorkOrderId = workOrderId;
            BatchId = batchId;
            WorkOrderFolderId = workOrderFolderId;
            ImagesFolderId = imagesFolderId;
            PdfFolderId = pdfFolderId;
            ExpensesFolderId = expensesFolderId;
            Files = files ?? new List<PlannedFileInfo>();
        }

        /// <summary>The WO number/id this batch belongs to.</summary>
        public string WorkOrderId { get; set; } = string.Empty;

        /// <summary>Server-generated batch id (used in blob prefix).</summary>
        public string BatchId { get; set; } = string.Empty;

        /// <summary>Drive folder id for the WO root.</summary>
        public string WorkOrderFolderId { get; set; } = string.Empty;

        /// <summary>Drive folder id for images (receipts/parts photos).</summary>
        public string ImagesFolderId { get; set; } = string.Empty;

        /// <summary>Drive folder id for PDFs.</summary>
        public string PdfFolderId { get; set; } = string.Empty;

        /// <summary>Drive folder id for expenses (if you separate them), optional.</summary>
        public string ExpensesFolderId { get; set; } = string.Empty;

        /// <summary>All files in this batch with chosen container + blob path.</summary>
        public List<PlannedFileInfo> Files { get; set; } = new();
    }
}
