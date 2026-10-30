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
        // Inicio logs signature
        _logger.LogInformation(
            "[WEBHOOK SIGNATURE][DIAG] Start validation rawBodyLength={RawBodyLength} headerPresent={HeaderPresent} headerLength={HeaderLength}",
            rawBody?.Length ?? 0,
            !string.IsNullOrWhiteSpace(signatureHeader),
            signatureHeader?.Length ?? 0);
        // Fin logs signature

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            // Inicio logs signature
            _logger.LogWarning(
                "[WEBHOOK SIGNATURE][DIAG] Missing signature header. rawBodyLength={RawBodyLength}",
                rawBody?.Length ?? 0);
            // Fin logs signature

            _logger.LogWarning("[WEBHOOK SIGNATURE] Missing signature header X-Hub-Signature-256.");
            return false;
        }

        if (!signatureHeader.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Inicio logs signature
            _logger.LogWarning(
                "[WEBHOOK SIGNATURE][DIAG] Invalid prefix expectedPrefix='{ExpectedPrefix}' receivedHeaderStart='{ReceivedStart}' receivedHeader='{ReceivedHeader}'",
                SignaturePrefix,
                signatureHeader.Length >= 16 ? signatureHeader.Substring(0, 16) : signatureHeader,
                signatureHeader);
            // Fin logs signature

            _logger.LogWarning("[WEBHOOK SIGNATURE] Signature header does not start with '{Prefix}'.", SignaturePrefix);
            return false;
        }

        string hex = signatureHeader.Substring(SignaturePrefix.Length).Trim();
        if (hex.Length == 0)
        {
            // Inicio logs signature
            _logger.LogWarning(
                "[WEBHOOK SIGNATURE][DIAG] Hex empty after prefix removal. receivedHeader='{ReceivedHeader}'",
                signatureHeader);
            // Fin logs signature

            _logger.LogWarning("[WEBHOOK SIGNATURE] Signature hex is empty.");
            return false;
        }

        if (!TryDecodeHex(hex, out byte[] expectedSignature))
        {
            // Inicio logs signature
            _logger.LogWarning(
                "[WEBHOOK SIGNATURE][DIAG] Hex decode failed. hexLength={HexLength} hexStart='{HexStart}'",
                hex.Length,
                hex.Length >= 16 ? hex.Substring(0, 16) : hex);
            // Fin logs signature

            _logger.LogWarning("[WEBHOOK SIGNATURE] Signature hex is not valid hex.");
            return false;
        }

        string? appSecret = _configuration[AppSecretConfigKey];

        // Inicio logs signature
        _logger.LogInformation(
            "[WEBHOOK SIGNATURE][DIAG] AppSecret config key='{Key}' isNull={IsNull} isEmptyOrWhiteSpace={IsEmptyOrWhiteSpace} length={Length} isPlaceholder={IsPlaceholder}",
            AppSecretConfigKey,
            appSecret is null,
            string.IsNullOrWhiteSpace(appSecret),
            appSecret?.Length ?? 0,
            string.Equals(appSecret, "__REEMPLAZAR__", StringComparison.OrdinalIgnoreCase));
        // Fin logs signature

        if (string.IsNullOrWhiteSpace(appSecret) || string.Equals(appSecret, "__REEMPLAZAR__", StringComparison.OrdinalIgnoreCase))
        {
            // Inicio logs signature
            _logger.LogError(
                "[WEBHOOK SIGNATURE][DIAG] AppSecret invalid. key='{Key}' isNull={IsNull} isEmptyOrWhiteSpace={IsEmptyOrWhiteSpace} isPlaceholder={IsPlaceholder}",
                AppSecretConfigKey,
                appSecret is null,
                string.IsNullOrWhiteSpace(appSecret),
                string.Equals(appSecret, "__REEMPLAZAR__", StringComparison.OrdinalIgnoreCase));
            // Fin logs signature

            _logger.LogError("[WEBHOOK SIGNATURE] AppSecret is missing or not configured (key: {Key}).", AppSecretConfigKey);
            return false;
        }

        byte[] secretBytes = Encoding.UTF8.GetBytes(appSecret);
        byte[] computed;
        using (var hmac = new HMACSHA256(secretBytes))
        {
            computed = hmac.ComputeHash(rawBody);
        }

        // Inicio logs signature
        _logger.LogInformation(
            "[WEBHOOK SIGNATURE][DIAG] HMAC computed computedLength={ComputedLength} expectedLength={ExpectedLength} expectedHexLength={ExpectedHexLength}",
            computed.Length,
            expectedSignature.Length,
            hex.Length);
        // Fin logs signature

        if (expectedSignature.Length != computed.Length)
        {
            // Inicio logs signature
            _logger.LogWarning(
                "[WEBHOOK SIGNATURE][DIAG] Length mismatch expectedLength={ExpectedLength} computedLength={ComputedLength}",
                expectedSignature.Length,
                computed.Length);
            // Fin logs signature

            _logger.LogWarning("[WEBHOOK SIGNATURE] Signature length mismatch.");
            return false;
        }

        bool ok = CryptographicOperations.FixedTimeEquals(expectedSignature, computed);

        // Inicio logs signature
        _logger.LogWarning(
            "[WEBHOOK SIGNATURE][DIAG] FixedTimeEquals ok={Ok}",
            ok);
        // Fin logs signature

        if (!ok)
        {
            _logger.LogWarning("[WEBHOOK SIGNATURE] Signature mismatch.");

            // Inicio logs signature
            _logger.LogWarning(
                "[WEBHOOK SIGNATURE][DIAG] Signature mismatch details receivedHeader='{ReceivedHeader}' receivedHexStart='{ReceivedHexStart}'",
                signatureHeader,
                hex.Length >= 16 ? hex.Substring(0, 16) : hex);
            // Fin logs signature
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
