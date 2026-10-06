using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace VigiShield.Infrastructure.Services;

/// <summary>
/// Uploads media (e.g. app screenshots) to the Cloudflare R2 bucket and returns
/// the public URL served from R2:PublicBaseUrl. Returns null when R2 is not
/// configured so callers can degrade gracefully.
/// </summary>
public class R2Service
{
    private readonly IConfiguration _config;
    private readonly ILogger<R2Service> _logger;

    public R2Service(IConfiguration config, ILogger<R2Service> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_config["R2:Endpoint"]) &&
        !string.IsNullOrEmpty(_config["R2:AccessKeyId"]) &&
        !string.IsNullOrEmpty(_config["R2:SecretAccessKey"]) &&
        !string.IsNullOrEmpty(_config["R2:Bucket"]);

    public async Task<string?> UploadAsync(Stream content, string contentType, string prefix, string ext)
    {
        if (!IsConfigured) return null;
        try
        {
            var s3Config = new AmazonS3Config
            {
                ServiceURL = _config["R2:Endpoint"],
                ForcePathStyle = true,
                // R2 doesn't support the SDK-v4 default trailing CRC checksums.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            };
            using var client = new AmazonS3Client(
                _config["R2:AccessKeyId"], _config["R2:SecretAccessKey"], s3Config);

            var key = $"{prefix}/{DateTime.UtcNow:yyyyMMdd}_{Guid.NewGuid():N}{ext}";
            var request = new PutObjectRequest
            {
                BucketName = _config["R2:Bucket"],
                Key = key,
                InputStream = content,
                ContentType = contentType,
                DisablePayloadSigning = true,
            };
            await client.PutObjectAsync(request);

            var publicBase = (_config["R2:PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
            return $"{publicBase}/{key}";
        }
        catch (Exception e)
        {
            _logger.LogError(e, "R2 upload failed");
            return null;
        }
    }

    /// <summary>
    /// Borra del bucket los archivos de estas URLs públicas (fotos y clips de
    /// eventos). Solo toca las que cuelgan de R2:PublicBaseUrl o del dominio
    /// público del bucket; las demás se ignoran. Devuelve cuántas se borraron.
    /// </summary>
    public async Task<int> DeleteByUrlsAsync(IEnumerable<string> urls, CancellationToken ct = default)
    {
        if (!IsConfigured) return 0;
        var bases = new[] { (_config["R2:PublicBaseUrl"] ?? "").TrimEnd('/'), "https://bucket.vigishield.app" }
            .Where(b => b.Length > 0).Distinct().ToArray();
        var keys = urls
            .Select(u => bases.FirstOrDefault(b => u.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase)) is { } b
                ? Uri.UnescapeDataString(u[(b.Length + 1)..].Split('?')[0]) : null)
            .Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (keys.Count == 0) return 0;

        var s3Config = new AmazonS3Config
        {
            ServiceURL = _config["R2:Endpoint"],
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        using var client = new AmazonS3Client(_config["R2:AccessKeyId"], _config["R2:SecretAccessKey"], s3Config);
        var borradas = 0;
        foreach (var lote in keys.Chunk(1000))
        {
            var resp = await client.DeleteObjectsAsync(new DeleteObjectsRequest
            {
                BucketName = _config["R2:Bucket"],
                Objects = lote.Select(k => new KeyVersion { Key = k }).ToList(),
            }, ct);
            borradas += resp.DeletedObjects?.Count ?? 0;
        }
        return borradas;
    }
}
