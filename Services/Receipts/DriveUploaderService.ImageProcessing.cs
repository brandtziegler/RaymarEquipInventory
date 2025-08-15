using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace RaymarEquipmentInventory.Services
{
    public partial class DriveUploaderService
    {
        private static readonly JpegEncoder Jpeg85 = new JpegEncoder { Quality = 85 };

        private static async Task<MemoryStream> NormalizeImageAsync(IFormFile file, CancellationToken ct)
        {
            // Read original bytes
            using var inStream = new MemoryStream();
            await file.CopyToAsync(inStream, ct);
            inStream.Position = 0;

            // Optionally detect HEIC and convert (requires HEIC plugin)
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            // if (ext == ".heic") { /* decode with plugin -> Image */ }

            // Load with ImageSharp
            inStream.Position = 0;
            using var image = await Image.LoadAsync(inStream, ct);

            // Heuristic resize: if width > 2000px, scale down
            const int targetMaxWidth = 2000;
            if (image.Width > targetMaxWidth)
            {
                var targetHeight = (int)Math.Round(image.Height * (targetMaxWidth / (double)image.Width));
                image.Mutate(x => x.Resize(targetMaxWidth, targetHeight));
            }

            // Save JPEG @ ~85%
            var outStream = new MemoryStream();
            await image.SaveAsJpegAsync(outStream, Jpeg85, ct);
            outStream.Position = 0;

            // If still > 4 MB (rare), you can reduce quality to ~80 or 75 and re-save.
            return outStream;
        }
    }
}
