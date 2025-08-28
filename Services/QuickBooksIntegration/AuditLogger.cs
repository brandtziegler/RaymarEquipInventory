using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Data;                    // for SqlDbType
using System.Threading;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;


namespace RaymarEquipmentInventory.Services
{
    /// <summary>
    /// Thin wrapper over stored procs:
    ///   - dbo.Qbwc_StartSession(@RunId, @QbwcUser, @CompanyFile, @Ticket)
    ///   - dbo.Qbwc_EndSession(@RunId, @LastError)
    ///   - dbo.Qbwc_LogMessage(@RunId, @Method, @Direction, @StatusCode, @HResult, @Message, @CompanyFile, @PayloadXml)
    /// </summary>
    public sealed class AuditLogger : IAuditLogger
    {
        private readonly RaymarInventoryDBContext _context;

        public AuditLogger(RaymarInventoryDBContext context)
        {
            _context = context;
        }

        public async Task StartSessionAsync(Guid runId, string? qbwcUser, string? companyFile, string ticket, CancellationToken ct = default)
        {
            var p = new[]
            {
                new SqlParameter("@RunId", SqlDbType.UniqueIdentifier){ Value = runId },
                new SqlParameter("@QbwcUser", SqlDbType.NVarChar, 100){ Value = (object?)qbwcUser ?? DBNull.Value },
                new SqlParameter("@CompanyFile", SqlDbType.NVarChar, 400){ Value = (object?)companyFile ?? DBNull.Value },
                new SqlParameter("@Ticket", SqlDbType.NVarChar, 200){ Value = ticket }
            };

            await _context.Database.ExecuteSqlRawAsync(
                "EXEC dbo.Qbwc_StartSession @RunId, @QbwcUser, @CompanyFile, @Ticket",
                p, ct);
        }

        public async Task EndSessionAsync(Guid runId, string? lastError = null, CancellationToken ct = default)
        {
            var p = new[]
            {
                new SqlParameter("@RunId", SqlDbType.UniqueIdentifier){ Value = runId },
                new SqlParameter("@LastError", SqlDbType.NVarChar, -1){ Value = (object?)lastError ?? DBNull.Value },
            };

            await _context.Database.ExecuteSqlRawAsync(
                "EXEC dbo.Qbwc_EndSession @RunId, @LastError",
                p, ct);
        }

        public async Task LogMessageAsync(
            Guid runId,
            string method,
            string direction,
            int? statusCode = null,
            string? hresult = null,
            string? message = null,
            string? companyFile = null,
            string? payloadXml = null,
            CancellationToken ct = default)
        {
            var p = new[]
            {
                new SqlParameter("@RunId", SqlDbType.UniqueIdentifier){ Value = runId },
                new SqlParameter("@Method", SqlDbType.VarChar, 40){ Value = method },
                new SqlParameter("@Direction", SqlDbType.VarChar, 10){ Value = direction },
                new SqlParameter("@StatusCode", SqlDbType.Int){ Value = (object?)statusCode ?? DBNull.Value },
                new SqlParameter("@HResult", SqlDbType.NVarChar, 50){ Value = (object?)hresult ?? DBNull.Value },
                new SqlParameter("@Message", SqlDbType.NVarChar, 4000){ Value = (object?)message ?? DBNull.Value },
                new SqlParameter("@CompanyFile", SqlDbType.NVarChar, 400){ Value = (object?)companyFile ?? DBNull.Value },
                new SqlParameter("@PayloadXml", SqlDbType.Xml){ Value = (object?)payloadXml ?? DBNull.Value }
            };

            await _context.Database.ExecuteSqlRawAsync(
                "EXEC dbo.Qbwc_LogMessage @RunId, @Method, @Direction, @StatusCode, @HResult, @Message, @CompanyFile, @PayloadXml",
                p, ct);
        }
    }
}
