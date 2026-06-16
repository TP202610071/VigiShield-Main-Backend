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
}
