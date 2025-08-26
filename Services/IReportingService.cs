using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IReportingService
    {
        /// <summary>Query invoice rows for a work order (detail or summed view).</summary>
        Task<IReadOnlyList<InvoiceCSVRow>> GetInvoiceRowsAsync(int sheetId, bool summed, CancellationToken ct = default);

        /// <summary>Builds an in-memory CSV (UTF-8, no BOM) for the invoice rows.</summary>

       Task<MemoryStream> BuildInvoiceXlsxAsync(
            int sheetId, bool summed, CancellationToken ct = default);

        /// <summary>Email the invoice CSV (Resend) to configured recipients.</summary>
        Task SendInvoiceXlsxAsync(int sheetId, bool summed, CancellationToken ct = default);

        /// <summary>Returns (filename, csv stream) for download or email.</summary>
        Task<(string FileName, MemoryStream Xlsx)> GetInvoiceXlsxPackageAsync(
            int sheetId, bool summed, CancellationToken ct = default);

        Task<MemoryStream> BuildInvoiceIifAsync(int sheetId, bool summed, CancellationToken ct = default);

        Task<(string FileName, MemoryStream Iif)> GetInvoiceIifPackageAsync(
            int sheetId, bool summed, CancellationToken ct = default);

        Task SendInvoiceIIFAsync(int sheetId, bool summed, CancellationToken ct = default);
    }
}
