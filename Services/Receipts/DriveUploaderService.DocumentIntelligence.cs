using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.DocumentIntelligence;

namespace RaymarEquipmentInventory.Services
{
    public partial class DriveUploaderService
    {
        private DocumentIntelligenceClient CreateDocIntelClient()
        {
            var endpoint = Environment.GetEnvironmentVariable("AZURE_FORMRECOGNIZER_ENDPOINT") ?? "";
            var key = Environment.GetEnvironmentVariable("AZURE_FORMRECOGNIZER_KEY") ?? "";

            return new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key));
        }

        private static async Task<AnalyzeResult> AnalyzeReceiptAsync(
            DocumentIntelligenceClient client,
            string modelId,
            Stream imageStream,
            CancellationToken ct)
        {
            var op = await client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                modelId,
                BinaryData.FromStream(imageStream),
                cancellationToken: ct);

            return op.Value;
        }

        private string GetModelId() =>
            Environment.GetEnvironmentVariable("AZURE_FORMRECOGNIZER_MODEL") ?? "";
    }
}

