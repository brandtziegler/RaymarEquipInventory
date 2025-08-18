namespace RaymarEquipmentInventory.DTOs
{
    public record ClientFileDto(string Name, string? Type, long? SizeBytes, string? ContentType);

    public record StartBlobBatchRequest(
        string WorkOrderId,
        string? WorkOrderFolderId,
        string? PdfFolderId,
        string? ExpensesFolderId,
        string? ImagesFolderId,
        string? TestPrefix,
        string? BatchId,
        string? CustPath,
        List<ClientFileDto> Files,
        int? ClientParallelism // <— new (optional)
    );

    /// <summary>Per-file plan returned by StartBlobBatch, including SAS for direct PUT.</summary>


    public record StartBlobFile(string Name, string Container, string BlobPath, string? ContentType, string SasUrl);
    public record StartBlobBatchResponse(
        string WorkOrderId, string BatchId, string? TestPrefixApplied, int RecommendedParallelism, IReadOnlyList<StartBlobFile> Files);

    public record FinalizeBlobFileDto(string Name, string Container, string BlobPath);
    public record FinalizeBlobBatchRequest(string WorkOrderId, string BatchId, List<FinalizeBlobFileDto> Files);
    public record FinalizeBlobBatchResponse(string WorkOrderId, string BatchId, int PlannedCount, int UploadedOk, IReadOnlyList<object> UploadedFailed, bool FinalizeOk);
}
