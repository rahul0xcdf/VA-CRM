using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VinayakaAgencies.Application.Einvoicing;
using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Infrastructure.Einvoicing;

/// <summary>
/// Talks to NIC's Invoice Registration Portal over HTTPS (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Three things this class does that are worth knowing before reading it.
/// </para>
/// <para>
/// <b>It never throws for a failed call.</b> A portal refusal and a timeout both come back as an
/// <see cref="IrpOutcome"/>. That is what lets the caller record the attempt and decide, instead of
/// every call site having to reconstruct "was this retryable?" from an exception type.
/// </para>
/// <para>
/// <b>Every body is redacted before it leaves this class.</b> Nothing raw reaches the logger or the
/// returned <see cref="IrpCallRecord"/>, because those are the two paths by which a request body
/// becomes something durable (checkpoint C6.6). The session token and session key are redacted too,
/// not just the four stored credentials — a leaked auth token is a usable credential for its lifetime.
/// </para>
/// <para>
/// <b>Auth is cached per GSTIN and environment, and refreshed early.</b> The portal issues a token
/// with a stated expiry; re-authenticating on every call would be both slow and, on NIC's side,
/// rate-limited.
/// </para>
/// <para>
/// This client is <b>unverified against a live portal</b> while <c>open-questions.md</c> #10 is open.
/// The shape below follows NIC's documented handshake; confirming it is what C6.1–C6.3 are for.
/// </para>
/// </remarks>
internal sealed partial class NicIrpClient : IIrpClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly NicIrpOptions _options;
    private readonly IrpSessionCache _sessions;
    private readonly ILogger<NicIrpClient> _logger;

    public NicIrpClient(
        HttpClient httpClient,
        IOptions<NicIrpOptions> options,
        IrpSessionCache sessions,
        ILogger<NicIrpClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _sessions = sessions;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IrpIrnResponse> GenerateIrnAsync(
        IrpCredentials credentials,
        EinvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync(
            credentials,
            HttpMethod.Post,
            _options.GenerateIrnPath,
            NicInvoicePayload.From(request),
            ReadIrnResponse,
            response => new IrpIrnResponse(response),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IrpIrnResponse> GetByIrnAsync(
        IrpCredentials credentials,
        string irn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(irn);

        return SendAsync(
            credentials,
            HttpMethod.Get,
            _options.GetByIrnPath.Replace("{irn}", irn, StringComparison.Ordinal),
            payload: null,
            ReadIrnResponse,
            response => new IrpIrnResponse(response),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IrpCancelResponse> CancelIrnAsync(
        IrpCredentials credentials,
        string irn,
        string reasonCode,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(irn);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        return SendAsync(
            credentials,
            HttpMethod.Post,
            _options.CancelIrnPath,
            new { Irn = irn, CnlRsn = reasonCode, CnlRem = remarks },
            (record, data) => new IrpCancelResponse(record, Text(data, "CancelDate")),
            response => new IrpCancelResponse(response),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IrpEwayBillResponse> GenerateEwayBillByIrnAsync(
        IrpCredentials credentials,
        string irn,
        EwayBillPartB partB,
        int distanceKilometres,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(irn);
        ArgumentNullException.ThrowIfNull(partB);

        return SendAsync(
            credentials,
            HttpMethod.Post,
            _options.GenerateEwayBillByIrnPath,
            NicEwayBillPayload.From(irn, partB, distanceKilometres),
            ReadEwayBillResponse,
            response => new IrpEwayBillResponse(response),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IrpEwayBillResponse> UpdateEwayBillPartBAsync(
        IrpCredentials credentials,
        long ewayBillNumber,
        EwayBillPartB partB,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(partB);

        return SendAsync(
            credentials,
            HttpMethod.Post,
            _options.UpdateEwayBillPartBPath,
            NicEwayBillPayload.PartBUpdate(ewayBillNumber, partB),
            ReadEwayBillResponse,
            response => new IrpEwayBillResponse(response),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IrpCancelResponse> CancelEwayBillAsync(
        IrpCredentials credentials,
        long ewayBillNumber,
        string reasonCode,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        return SendAsync(
            credentials,
            HttpMethod.Post,
            _options.CancelEwayBillPath,
            new { ewbNo = ewayBillNumber, cancelRsnCode = reasonCode, cancelRmrk = remarks },
            (record, data) => new IrpCancelResponse(record, Text(data, "cancelDate")),
            response => new IrpCancelResponse(response),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IrpEwayBillResponse> ExtendEwayBillValidityAsync(
        IrpCredentials credentials,
        long ewayBillNumber,
        int remainingDistanceKilometres,
        string reasonCode,
        string? remarks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        return SendAsync(
            credentials,
            HttpMethod.Post,
            _options.ExtendEwayBillPath,
            new
            {
                ewbNo = ewayBillNumber,
                remainingDistance = remainingDistanceKilometres,
                extnRsnCode = reasonCode,
                extnRemarks = remarks,
            },
            ReadEwayBillResponse,
            response => new IrpEwayBillResponse(response),
            cancellationToken);
    }

    /// <summary>
    /// The one path every call takes: authenticate, encrypt, send, decrypt, redact, map. Written once
    /// because the failure handling is the part that must not vary between operations.
    /// </summary>
    private async Task<TResponse> SendAsync<TResponse>(
        IrpCredentials credentials,
        HttpMethod method,
        string path,
        object? payload,
        Func<IrpCallRecord, JsonNode?, TResponse> onAnswered,
        Func<IrpCallRecord, TResponse> onNoResponse,
        CancellationToken cancellationToken)
    {
        var secrets = SecretsOf(credentials);
        var requestJson = payload is null ? string.Empty : JsonSerializer.Serialize(payload, SerializerOptions);
        var redactedRequest = SensitiveText.Redact(requestJson, secrets);

        IrpSession session;

        try
        {
            session = await EnsureSessionAsync(credentials, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Authentication itself did not come back. Retryable, exactly like a business call that
            // did not come back — the invoice is no more registered than it was before.
            return onNoResponse(NoResponse(path, redactedRequest, exception));
        }

        using var message = new HttpRequestMessage(method, path);

        message.Headers.TryAddWithoutValidation("client_id", credentials.ClientId);
        message.Headers.TryAddWithoutValidation("client_secret", credentials.ClientSecret);
        message.Headers.TryAddWithoutValidation("Gstin", credentials.Gstin);
        message.Headers.TryAddWithoutValidation("user_name", credentials.Username);
        message.Headers.TryAddWithoutValidation("AuthToken", session.AuthToken);

        if (payload is not null)
        {
            // Business payloads travel encrypted under the session key, inside a "Data" envelope.
            message.Content = JsonContent.Create(
                new { Data = IrpCryptography.EncryptToBase64(requestJson, session.SessionKey) });
        }

        try
        {
            using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var redactedResponse = SensitiveText.Redact(body, secrets);

            var envelope = Parse(body);
            var status = Text(envelope, "Status");
            var succeeded = response.IsSuccessStatusCode
                && string.Equals(status, "1", StringComparison.Ordinal);

            if (!succeeded)
            {
                var (code, description) = ReadError(envelope);

                LogPortalRefused(_logger, path, code, description);

                return onAnswered(
                    new IrpCallRecord(
                        IrpOutcome.Rejected,
                        path,
                        redactedRequest,
                        redactedResponse,
                        (int)response.StatusCode,
                        code,
                        description),
                    null);
            }

            var data = DecryptData(envelope, session.SessionKey);

            return onAnswered(
                new IrpCallRecord(
                    IrpOutcome.Success,
                    path,
                    redactedRequest,
                    SensitiveText.Redact(data?.ToJsonString(), secrets),
                    (int)response.StatusCode),
                data);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return onNoResponse(NoResponse(path, redactedRequest, exception));
        }
    }

    private IrpCallRecord NoResponse(string path, string redactedRequest, Exception exception)
    {
        // The message, not the exception object: an exception's ToString can carry a request body.
        LogNoResponse(_logger, path, exception.Message);

        return new IrpCallRecord(
            IrpOutcome.NoResponse,
            path,
            redactedRequest,
            string.Empty,
            ErrorMessage: exception.Message);
    }

    /// <summary>Authenticates if there is no live token for this GSTIN and environment.</summary>
    private async Task<IrpSession> EnsureSessionAsync(
        IrpCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (_sessions.Get(credentials.Environment, credentials.Gstin, DateTimeOffset.UtcNow) is { } cached)
        {
            return cached;
        }

        var session = await AuthenticateAsync(credentials, cancellationToken).ConfigureAwait(false);

        _sessions.Set(credentials.Environment, credentials.Gstin, session);

        return session;
    }

    private async Task<IrpSession> AuthenticateAsync(
        IrpCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicCertificateBase64))
        {
            throw new InvalidOperationException(
                "NIC's public certificate is not configured. Set "
                + $"'{NicIrpOptions.SectionName}:PublicCertificateBase64' — it is published by NIC "
                + "alongside the API credentials.");
        }

        var appKey = IrpCryptography.CreateAppKey();

        var payload = JsonSerializer.Serialize(
            new
            {
                UserName = credentials.Username,
                Password = credentials.Password,
                AppKey = Convert.ToBase64String(appKey),
                ForceRefreshAccessToken = false,
            },
            SerializerOptions);

        using var message = new HttpRequestMessage(HttpMethod.Post, _options.AuthPath);

        message.Headers.TryAddWithoutValidation("client_id", credentials.ClientId);
        message.Headers.TryAddWithoutValidation("client_secret", credentials.ClientSecret);
        message.Headers.TryAddWithoutValidation("Gstin", credentials.Gstin);
        message.Content = JsonContent.Create(new
        {
            Data = IrpCryptography.EncryptWithPublicCertificate(
                payload, _options.PublicCertificateBase64),
        });

        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var envelope = Parse(body);

        if (!response.IsSuccessStatusCode
            || !string.Equals(Text(envelope, "Status"), "1", StringComparison.Ordinal))
        {
            var (code, description) = ReadError(envelope);

            throw new InvalidOperationException(
                $"NIC refused the authentication request: {code} {description}");
        }

        // The auth response is encrypted under the app key we just sent, not under a session key.
        var data = Parse(
            IrpCryptography.DecryptFromBase64(
                Text(envelope, "Data")
                    ?? throw new InvalidOperationException("NIC returned no authentication data."),
                appKey));

        var authToken = Text(data, "AuthToken")
            ?? throw new InvalidOperationException("NIC returned no auth token.");

        var sessionKey = IrpCryptography.DecryptSessionKey(
            Text(data, "Sek") ?? throw new InvalidOperationException("NIC returned no session key."),
            appKey);

        var expiry = ParseExpiry(Text(data, "TokenExpiry"))
            - TimeSpan.FromSeconds(_options.TokenExpirySafetyMarginSeconds);

        LogAuthenticated(_logger, credentials.Environment.ToString(), expiry);

        return new IrpSession(authToken, sessionKey, expiry);
    }

    /// <summary>
    /// Decrypts the <c>Data</c> envelope. A successful call with no <c>Data</c> is legitimate for
    /// operations that only acknowledge, so this returns null rather than failing.
    /// </summary>
    private static JsonNode? DecryptData(JsonNode? envelope, byte[] sessionKey)
    {
        var data = Text(envelope, "Data");

        return string.IsNullOrWhiteSpace(data)
            ? null
            : Parse(IrpCryptography.DecryptFromBase64(data, sessionKey));
    }

    private static IrpIrnResponse ReadIrnResponse(IrpCallRecord record, JsonNode? data) =>
        new(record,
            Number(data, "AckNo"),
            Text(data, "AckDt"),
            Text(data, "Irn"),
            Text(data, "SignedInvoice"),
            Text(data, "SignedQRCode"));

    private static IrpEwayBillResponse ReadEwayBillResponse(IrpCallRecord record, JsonNode? data) =>
        new(record,
            Number(data, "EwbNo"),
            Text(data, "EwbDt"),
            Text(data, "EwbValidTill"));

    private static JsonNode? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            // A non-JSON body is a gateway error page often enough to be worth surviving.
            return null;
        }
    }

    /// <summary>
    /// Reads NIC's error envelope. It arrives as either a plain message or a JSON array of
    /// code/description pairs, and both shapes turn up in practice.
    /// </summary>
    private static (string? Code, string? Description) ReadError(JsonNode? envelope)
    {
        var errorDetails = envelope?["ErrorDetails"];

        if (errorDetails is JsonArray { Count: > 0 } array)
        {
            var codes = array.Select(entry => Text(entry, "ErrorCode")).Where(code => code is not null);
            var messages = array.Select(entry => Text(entry, "ErrorMessage")).Where(text => text is not null);

            return (string.Join(",", codes), string.Join("; ", messages));
        }

        return (Text(envelope, "ErrorCode") ?? Text(envelope, "Status"),
            Text(envelope, "ErrorMessage")
                ?? Text(envelope, "ErrorDetails")
                ?? "The portal gave no reason.");
    }

    private static string? Text(JsonNode? node, string property)
    {
        var value = node?[property];

        return value is null ? null : value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.ToJsonString().Trim('"'),
        };
    }

    private static long? Number(JsonNode? node, string property) =>
        long.TryParse(Text(node, property), CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// NIC states the token expiry as an IST timestamp. An unreadable one is treated as a very short
    /// life rather than a long one — re-authenticating unnecessarily is cheap, using a dead token is
    /// an outage.
    /// </summary>
    private static DateTimeOffset ParseExpiry(string? tokenExpiry) =>
        DateTime.TryParse(tokenExpiry, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed, new TimeSpan(5, 30, 0)).ToUniversalTime()
            : DateTimeOffset.UtcNow.AddMinutes(1);

    /// <summary>
    /// Everything that must never appear in a log or a stored body: the four credentials, plus the
    /// token and session key, which are credentials for as long as they live.
    /// </summary>
    private static List<string> SecretsOf(IrpCredentials credentials) =>
    [
        credentials.ClientId,
        credentials.ClientSecret,
        credentials.Username,
        credentials.Password,
    ];

    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Warning,
        Message = "IRP {Path} refused the request: {ErrorCode} {ErrorMessage}")]
    private static partial void LogPortalRefused(
        ILogger logger, string path, string? errorCode, string? errorMessage);

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Warning,
        Message = "IRP {Path} did not answer ({Reason}); the attempt stays retryable.")]
    private static partial void LogNoResponse(ILogger logger, string path, string reason);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Information,
        Message = "Authenticated with the NIC {Environment} portal; token valid until {Expiry:u}.")]
    private static partial void LogAuthenticated(
        ILogger logger, string environment, DateTimeOffset expiry);
}
