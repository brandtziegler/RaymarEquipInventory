using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public partial class DriveUploaderService
    {
        private static async Task<MemoryStream> BuildCsvAsync(
            System.Collections.Generic.List<ReceiptCsvRow> rows,
            string fileName,
            CancellationToken ct)
        {
            var ms = new MemoryStream();
            await using var writer = new StreamWriter(
                ms,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 1024,
                leaveOpen: true);

            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(rows, ct);
            await writer.FlushAsync();
            ms.Position = 0;
            return ms;
        }
    }
}
