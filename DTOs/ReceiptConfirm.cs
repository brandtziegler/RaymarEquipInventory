using System;
using System.Collections.Generic;
namespace RaymarEquipmentInventory.DTOs
{
    public class ReceiptConfirm
    {
        public string BatchId { get; set; } = "";
        public int ReceivedCount { get; set; }
        public int ProcessedCount { get; set; }
        public int NeedsReviewCount { get; set; }
        public string CsvFileName { get; set; } = "";
        public long CsvSizeBytes { get; set; }
        public List<string> Errors { get; set; } = new();
    }

}
