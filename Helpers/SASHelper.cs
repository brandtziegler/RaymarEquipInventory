using System;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace RaymarEquipmentInventory.Helpers
{
    public static class SASHelper
    {
        /// <summary>
        /// Generate a SAS URI that allows a client to PUT a single blob (Create/Write).
        /// Uses AZURE_STORAGE_CONNECTION_STRING from environment.
        /// </summary>
        public static Uri GenerateBlobPutSasUri(string container, string blobPath, TimeSpan? ttl = null)
        {
            var connStr = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
                         ?? throw new InvalidOperationException("AZURE_STORAGE_CONNECTION_STRING missing.");

            var svc = new BlobServiceClient(connStr);
            return GenerateBlobSasUriCore(svc, container, blobPath, ttl ?? TimeSpan.FromMinutes(15),
                                          BlobSasPermissions.Create | BlobSasPermissions.Write);
        }

        /// <summary>
        /// Same as GenerateBlobPutSasUri but returns a string.
        /// </summary>
        public static string GenerateBlobPutSas(string container, string blobPath, int ttlMinutes = 15)
            => GenerateBlobPutSasUri(container, blobPath, TimeSpan.FromMinutes(ttlMinutes)).ToString();

        /// <summary>
        /// Overload that takes an existing BlobServiceClient (e.g., injected).
        /// </summary>
        public static Uri GenerateBlobPutSasUri(BlobServiceClient svc, string container, string blobPath, TimeSpan? ttl = null)
            => GenerateBlobSasUriCore(svc, container, blobPath, ttl ?? TimeSpan.FromMinutes(15),
                                      BlobSasPermissions.Create | BlobSasPermissions.Write);

        /// <summary>
        /// Core builder for blob SAS URIs.
        /// </summary>
        private static Uri GenerateBlobSasUriCore(
            BlobServiceClient svc,
            string container,
            string blobPath,
            TimeSpan ttl,
            BlobSasPermissions perms)
        {
            if (string.IsNullOrWhiteSpace(container)) throw new ArgumentException("Container required.", nameof(container));
            if (string.IsNullOrWhiteSpace(blobPath)) throw new ArgumentException("Blob path required.", nameof(blobPath));

            // Normalize names
            container = container.Trim().ToLowerInvariant();
            blobPath = blobPath.Replace('\\', '/').TrimStart('/').Replace("//", "/");

            var containerClient = svc.GetBlobContainerClient(container);
            containerClient.CreateIfNotExists();

            var blobClient = containerClient.GetBlobClient(blobPath);

            if (!blobClient.CanGenerateSasUri)
                throw new InvalidOperationException("BlobClient cannot generate SAS URI. Ensure the client was created with shared-key creds (connection string).");

            var sas = new BlobSasBuilder
            {
                BlobContainerName = container,
                BlobName = blobPath,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.Add(ttl)
            };
            sas.SetPermissions(perms);

            return blobClient.GenerateSasUri(sas);
        }
    }
}

