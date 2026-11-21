using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Vyxum.Platform;

/// <summary>The outcome of <see cref="AgentConnectionGate.Check"/>.</summary>
public enum AgentConnectionDecision
{
    /// <summary>The connection may proceed.</summary>
    Allowed,

    /// <summary>A key is configured and the request carries none or a wrong one.</summary>
    InvalidKey,

    /// <summary>No key is configured and the request does not come from this computer.</summary>
    RemoteNotAllowed,
}

/// <summary>Decides whether a request may reach the agent hub (decision V-26).</summary>
public sealed partial class AgentConnectionGate(IOptionsMonitor<AgentOptions> options, ILogger<AgentConnectionGate> logger)
{
    /// <summary>Checks the request against the configured key or, without one, its remote address.</summary>
    public AgentConnectionDecision Check(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var key = options.CurrentValue.Key;
        if (string.IsNullOrWhiteSpace(key))
        {
            return IsLocal(context.Connection.RemoteIpAddress)
                ? AgentConnectionDecision.Allowed
                : AgentConnectionDecision.RemoteNotAllowed;
        }

        return KeysMatch(ReadKey(context.Request), key)
            ? AgentConnectionDecision.Allowed
            : AgentConnectionDecision.InvalidKey;
    }

    /// <summary>Rejects the request with 401 or 403 unless <see cref="Check"/> allows it.</summary>
    /// <returns>True when the request may continue.</returns>
    public bool Admit(HttpContext context)
    {
        var decision = Check(context);
        switch (decision)
        {
            case AgentConnectionDecision.Allowed:
                return true;
            case AgentConnectionDecision.InvalidKey:
                LogInvalidKey(logger, context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return false;
            default:
                LogRemoteNotAllowed(logger, context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return false;
        }
    }

    /// <summary>
    /// True for loopback addresses. A missing address means an in-process or local socket
    /// connection (test server, Unix socket, named pipe), which is local as well.
    /// </summary>
    internal static bool IsLocal(IPAddress? address)
    {
        if (address is null)
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// Reads the key from the header only. A query-string key is not accepted: query strings end up in request logs.
    /// </summary>
    private static string? ReadKey(HttpRequest request)
    {
        var header = request.Headers[AgentHubContract.KeyHeader].ToString();
        return header.Length > 0 ? header : null;
    }

    private static bool KeysMatch(string? supplied, string expected)
    {
        if (supplied is null)
        {
            return false;
        }

        // Hashing first makes the comparison independent of the key lengths as well.
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected agent connection from {RemoteAddress}: missing or invalid agent key.")]
    private static partial void LogInvalidKey(ILogger logger, IPAddress? remoteAddress);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected agent connection from {RemoteAddress}: no agent key is configured, so only local connections are accepted.")]
    private static partial void LogRemoteNotAllowed(ILogger logger, IPAddress? remoteAddress);
}
