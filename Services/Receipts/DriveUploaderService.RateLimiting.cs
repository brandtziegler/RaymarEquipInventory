using System;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public partial class DriveUploaderService
    {
        private static async Task RespectRateAsync(DateTime lastCallUtc, int minMsBetweenCalls, CancellationToken ct)
        {
            if (lastCallUtc == DateTime.MinValue) return;
            var elapsed = (int)(DateTime.UtcNow - lastCallUtc).TotalMilliseconds;
            var wait = minMsBetweenCalls - elapsed;
            if (wait > 0) await Task.Delay(wait, ct);
        }
    }
}
