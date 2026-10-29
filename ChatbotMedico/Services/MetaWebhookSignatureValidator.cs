using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ChatbotMedico.Services.Interfaces;

namespace ChatbotMedico.Services;

public sealed class MetaWebhookSignatureValidator : IWebhookSignatureValidator
{
    private const string SignaturePrefix = "sha256=";
    private const string AppSecretConfigKey = "WhatsApp:AppSecret";

    private readonly IConfiguration _configuration;
    private readonly ILogger<MetaWebhookSignatureValidator> _logger;

    public MetaWebhookSignatureValidator(IConfiguration configuration, ILogger<MetaWebhookSignatureValidator> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsValid(byte[] rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            _logger.LogWarning("[WEBHOOK SIGNATURE] Missing signature header X-Hub-Signature-256.");
            return false;
        }

        if (!signatureHeader.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[WEBHOOK SIGNATURE] Signature header does not start with '{Prefix}'.", SignaturePrefix);
            return false;
        }

        string hex = signatureHeader.Substring(SignaturePrefix.Length).Trim();
        if (hex.Length == 0)
        {
            _logger.LogWarning("[WEBHOOK SIGNATURE] Signature hex is empty.");
            return false;
        }

        if (!TryDecodeHex(hex, out byte[] expectedSignature))
        {
            _logger.LogWarning("[WEBHOOK SIGNATURE] Signature hex is not valid hex.");
            return false;
        }

        string? appSecret = _configuration[AppSecretConfigKey];
        if (string.IsNullOrWhiteSpace(appSecret) || string.Equals(appSecret, "__REEMPLAZAR__", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("[WEBHOOK SIGNATURE] AppSecret is missing or not configured (key: {Key}).", AppSecretConfigKey);
            return false;
        }

        byte[] secretBytes = Encoding.UTF8.GetBytes(appSecret);
        byte[] computed;
        using (var hmac = new HMACSHA256(secretBytes))
        {
            computed = hmac.ComputeHash(rawBody);
        }

        if (expectedSignature.Length != computed.Length)
        {
            _logger.LogWarning("[WEBHOOK SIGNATURE] Signature length mismatch.");
            return false;
        }

        bool ok = CryptographicOperations.FixedTimeEquals(expectedSignature, computed);
        if (!ok)
        {
            _logger.LogWarning("[WEBHOOK SIGNATURE] Signature mismatch.");
        }

        return ok;
    }

    private static bool TryDecodeHex(string hex, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();

        if (hex.Length % 2 != 0)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
