using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Vyxum.Platform;

/// <summary>Limits of the device settings.</summary>
public static class DeviceConfigLimits
{
    /// <summary>Maximum length of a device id.</summary>
    public const int IdMaxLength = 64;

    /// <summary>Maximum length of a device name.</summary>
    public const int NameMaxLength = 200;

    /// <summary>Maximum length of an agent name.</summary>
    public const int AgentNameMaxLength = 200;

    /// <summary>Maximum length of the transport or the protocol settings as JSON text.</summary>
    public const int SettingsMaxLength = 4000;

    /// <summary>Maximum length of the unit.</summary>
    public const int UnitMaxLength = 16;

    /// <summary>Maximum decimal places of capacity and division.</summary>
    public const int MaxDecimals = 6;

    /// <summary>The transport types the platform accepts. The protocol type is not checked by the platform.</summary>
    public static IReadOnlyList<string> TransportTypes { get; } = ["serial", "tcp", "simulator"];

    /// <summary>Ids that would collide with device routes.</summary>
    public static IReadOnlyList<string> ReservedIds { get; } = ["config", "discover"];
}

/// <summary>A device's settings as returned by <c>/api/devices/config</c>. Decimals are invariant-culture strings.</summary>
/// <param name="Id">The device id, for example <c>scale-1</c>.</param>
/// <param name="Name">The name shown on screens.</param>
/// <param name="AgentName">The agent (computer) that serves the device; null for the default agent.</param>
/// <param name="Transport">Transport settings, an object with at least <c>type</c> (<c>serial</c>, <c>tcp</c>, <c>simulator</c>).</param>
/// <param name="Protocol">Protocol settings, an object with at least <c>type</c>.</param>
/// <param name="Capacity">Maximum load (Max) in <paramref name="Unit"/>, for example "6".</param>
/// <param name="Division">Scale interval (e) in <paramref name="Unit"/>, for example "0.002".</param>
/// <param name="Unit">Unit of capacity and division, for example "kg".</param>
/// <param name="IsEnabled">Whether the agent runs the device.</param>
/// <param name="CreatedAt">UTC creation time.</param>
/// <param name="UpdatedAt">UTC time of the last change.</param>
public sealed record DeviceConfigDto(
    string Id,
    string Name,
    string? AgentName,
    JsonElement Transport,
    JsonElement Protocol,
    string? Capacity,
    string? Division,
    string? Unit,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Body of create and update requests. On update the id comes from the address and <see cref="Id"/> is ignored.</summary>
/// <param name="Id">The device id: lowercase letters, digits and hyphens, at most 64 characters.</param>
/// <param name="Name">The name; the id when empty.</param>
/// <param name="AgentName">The agent that serves the device; empty for the default agent.</param>
/// <param name="Transport">Transport settings (an object).</param>
/// <param name="Protocol">Protocol settings (an object).</param>
/// <param name="Capacity">Maximum load, a positive invariant-culture decimal string; optional.</param>
/// <param name="Division">Scale interval, a positive invariant-culture decimal string; optional.</param>
/// <param name="Unit">Unit of capacity and division; optional.</param>
/// <param name="IsEnabled">Whether the agent runs the device; true when missing.</param>
public sealed record DeviceConfigInput(
    string? Id,
    string? Name,
    string? AgentName,
    JsonElement? Transport,
    JsonElement? Protocol,
    string? Capacity = null,
    string? Division = null,
    string? Unit = null,
    bool? IsEnabled = null);

/// <summary>Outcome of a device settings change: the settings, a not-found marker, or validation errors.</summary>
public sealed class DeviceConfigResult
{
    private DeviceConfigResult()
    {
    }

    /// <summary>The settings, when the change succeeded.</summary>
    public DeviceConfigDto? Device { get; private init; }

    /// <summary>True when the device id is unknown.</summary>
    public bool NotFound { get; private init; }

    /// <summary>Validation errors keyed by field (<c>transport.portName</c>); values are UI string keys. Null unless invalid.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; private init; }

    internal static DeviceConfigResult Ok(DeviceConfigDto device) => new() { Device = device };

    internal static DeviceConfigResult Missing() => new() { NotFound = true };

    internal static DeviceConfigResult Invalid(Dictionary<string, string[]> errors) => new() { Errors = errors };
}

/// <summary>
/// Reads and changes the device settings in <c>plt_Devices</c> (decision V-39). After every change the agents that take
/// part (see <see cref="AgentDeviceAssignments"/>) are told to fetch their devices again, and the device names, capacities
/// and divisions shown in <see cref="LiveWeightStore"/> are refreshed.
/// </summary>
public sealed partial class DeviceConfigService
{
    private readonly PlatformDbContext _db;
    private readonly LiveWeightStore _store;
    private readonly AgentDeviceAssignments _assignments;
    private readonly IHubContext<AgentHub> _hub;
    private readonly TimeProvider _time;
    private readonly ILogger<DeviceConfigService> _logger;

    internal DeviceConfigService(
        PlatformDbContext db,
        LiveWeightStore store,
        AgentDeviceAssignments assignments,
        IHubContext<AgentHub> hub,
        TimeProvider time,
        ILogger<DeviceConfigService> logger)
    {
        _db = db;
        _store = store;
        _assignments = assignments;
        _hub = hub;
        _time = time;
        _logger = logger;
    }

    /// <summary>All devices, ordered by id.</summary>
    public async Task<IReadOnlyList<DeviceConfigDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var devices = await _db.Set<DeviceConfig>().AsNoTracking().OrderBy(d => d.Id).ToListAsync(cancellationToken);
        return [.. devices.Select(ToDto)];
    }

    /// <summary>One device, or null when the id is unknown.</summary>
    public async Task<DeviceConfigDto?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var device = await _db.Set<DeviceConfig>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        return device is null ? null : ToDto(device);
    }

    /// <summary>Creates a device.</summary>
    public async Task<DeviceConfigResult> CreateAsync(DeviceConfigInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = new Dictionary<string, string[]>();
        var id = ValidateId(input.Id, errors);
        var values = Validate(input, id ?? string.Empty, errors);
        if (id is not null && await _db.Set<DeviceConfig>().AnyAsync(d => d.Id == id, cancellationToken))
        {
            errors["id"] = [DeviceStrings.Code("IdTaken")];
        }

        if (id is null || values is null || errors.Count > 0)
        {
            return DeviceConfigResult.Invalid(errors);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var device = new DeviceConfig
        {
            Id = id,
            Name = values.Name,
            TransportJson = values.TransportJson,
            ProtocolJson = values.ProtocolJson,
            CreatedAt = now,
        };
        values.ApplyTo(device, now);
        _db.Add(device);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Created by a concurrent request after the check above.
            return DeviceConfigResult.Invalid(new Dictionary<string, string[]> { ["id"] = [DeviceStrings.Code("IdTaken")] });
        }

        await ChangedAsync(cancellationToken);
        return DeviceConfigResult.Ok(ToDto(device));
    }

    /// <summary>Changes a device. The id cannot change.</summary>
    public async Task<DeviceConfigResult> UpdateAsync(string id, DeviceConfigInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var device = await _db.Set<DeviceConfig>().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return DeviceConfigResult.Missing();
        }

        var errors = new Dictionary<string, string[]>();
        var values = Validate(input, device.Id, errors);
        if (values is null || errors.Count > 0)
        {
            return DeviceConfigResult.Invalid(errors);
        }

        values.ApplyTo(device, _time.GetUtcNow().UtcDateTime);
        await _db.SaveChangesAsync(cancellationToken);
        await ChangedAsync(cancellationToken);
        return DeviceConfigResult.Ok(ToDto(device));
    }

    /// <summary>Deletes a device. Returns false when the id is unknown.</summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var device = await _db.Set<DeviceConfig>().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null)
        {
            return false;
        }

        _db.Remove(device);
        await _db.SaveChangesAsync(cancellationToken);
        await ChangedAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// The enabled devices served by an agent (see <see cref="AgentDeviceAssignments"/>), in id order. Also refreshes the
    /// device details shown in <see cref="LiveWeightStore"/>.
    /// </summary>
    public async Task<IReadOnlyList<AgentDeviceConfig>> GetAgentDevicesAsync(string agentName, bool isDefault, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        var devices = await RefreshStoreAsync(cancellationToken);
        return
        [
            .. devices
                .Where(d => d.IsEnabled && AgentDeviceAssignments.Serves(d.AgentName, agentName, isDefault))
                .Select(d => new AgentDeviceConfig(d.Id, ParseObject(d.TransportJson), ParseObject(d.ProtocolJson))),
        ];
    }

    /// <summary>Loads every device and hands the enabled ones to <see cref="LiveWeightStore.SetConfiguredDevices"/>.</summary>
    internal async Task<List<DeviceConfig>> RefreshStoreAsync(CancellationToken cancellationToken)
    {
        var devices = await _db.Set<DeviceConfig>().AsNoTracking().OrderBy(d => d.Id).ToListAsync(cancellationToken);
        _store.SetConfiguredDevices(devices
            .Where(d => d.IsEnabled)
            .Select(d => new ConfiguredDevice(d.Id, d.Name, d.Capacity, d.Division, d.Unit)));
        return devices;
    }

    private async Task ChangedAsync(CancellationToken cancellationToken)
    {
        await RefreshStoreAsync(cancellationToken);
        var connections = _assignments.Connections;
        if (connections.Count == 0)
        {
            return;
        }

        try
        {
            await _hub.Clients.Clients(connections).SendAsync(AgentHubContract.DevicesChanged, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The agents also fetch their devices after every reconnect.
            LogNotifyFailed(_logger, exception.Message);
        }
    }

    private static string? ValidateId(string? text, Dictionary<string, string[]> errors)
    {
        var id = text?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            errors["id"] = [DeviceStrings.Code("IdRequired")];
            return null;
        }

        if (!IdPattern().IsMatch(id) || DeviceConfigLimits.ReservedIds.Contains(id, StringComparer.Ordinal))
        {
            errors["id"] = [DeviceStrings.Code("IdInvalid")];
            return null;
        }

        return id;
    }

    private static Values? Validate(DeviceConfigInput input, string id, Dictionary<string, string[]> errors)
    {
        var name = NullIfBlank(input.Name) ?? id;
        if (name.Length > DeviceConfigLimits.NameMaxLength)
        {
            errors["name"] = [DeviceStrings.Code("NameTooLong")];
        }

        var agentName = NullIfBlank(input.AgentName);
        if (agentName?.Length > DeviceConfigLimits.AgentNameMaxLength)
        {
            errors["agentName"] = [DeviceStrings.Code("AgentNameTooLong")];
        }

        var transport = ValidateTransport(input.Transport, errors);
        var protocol = ValidateProtocol(input.Protocol, errors);

        var capacity = ParseDecimal(input.Capacity, "capacity", errors);
        var division = ParseDecimal(input.Division, "division", errors);
        if (capacity is { } max && division is { } e && e > max)
        {
            errors["division"] = [DeviceStrings.Code("DivisionAboveCapacity")];
        }

        var unit = NullIfBlank(input.Unit);
        if (unit?.Length > DeviceConfigLimits.UnitMaxLength)
        {
            errors["unit"] = [DeviceStrings.Code("UnitTooLong")];
        }

        return errors.Count > 0 || transport is null || protocol is null
            ? null
            : new Values(name, agentName, transport, protocol, capacity, division, unit, input.IsEnabled ?? true);
    }

    private static string? ValidateTransport(JsonElement? element, Dictionary<string, string[]> errors)
    {
        if (element is not { ValueKind: JsonValueKind.Object } transport)
        {
            errors["transport"] = [DeviceStrings.Code("TransportRequired")];
            return null;
        }

        var json = JsonSerializer.Serialize(transport);
        if (json.Length > DeviceConfigLimits.SettingsMaxLength)
        {
            errors["transport"] = [DeviceStrings.Code("SettingsTooLong")];
            return null;
        }

        var type = GetString(transport, "type")?.Trim().ToLowerInvariant();
        if (type is null || !DeviceConfigLimits.TransportTypes.Contains(type, StringComparer.Ordinal))
        {
            errors["transport.type"] = [DeviceStrings.Code("TransportTypeInvalid")];
            return null;
        }

        var count = errors.Count;
        switch (type)
        {
            case "serial" when string.IsNullOrWhiteSpace(GetString(transport, "portName")):
                errors["transport.portName"] = [DeviceStrings.Code("PortNameRequired")];
                break;
            case "tcp":
                if (string.IsNullOrWhiteSpace(GetString(transport, "host")))
                {
                    errors["transport.host"] = [DeviceStrings.Code("HostRequired")];
                }

                if (GetPort(transport) is not (>= 1 and <= 65535))
                {
                    errors["transport.port"] = [DeviceStrings.Code("PortInvalid")];
                }

                break;
        }

        return errors.Count > count ? null : json;
    }

    private static string? ValidateProtocol(JsonElement? element, Dictionary<string, string[]> errors)
    {
        if (element is not { ValueKind: JsonValueKind.Object } protocol)
        {
            errors["protocol"] = [DeviceStrings.Code("ProtocolRequired")];
            return null;
        }

        var json = JsonSerializer.Serialize(protocol);
        if (json.Length > DeviceConfigLimits.SettingsMaxLength)
        {
            errors["protocol"] = [DeviceStrings.Code("SettingsTooLong")];
            return null;
        }

        if (string.IsNullOrWhiteSpace(GetString(protocol, "type")))
        {
            errors["protocol.type"] = [DeviceStrings.Code("ProtocolRequired")];
            return null;
        }

        return json;
    }

    /// <summary>A property read ignoring case, as the agent binds it.</summary>
    private static JsonElement? GetProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string name) =>
        GetProperty(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static int? GetPort(JsonElement element) => GetProperty(element, "port") switch
    {
        { ValueKind: JsonValueKind.Number } number when number.TryGetInt32(out var port) => port,
        { ValueKind: JsonValueKind.String } text when int.TryParse(text.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) => port,
        _ => null,
    };

    private static decimal? ParseDecimal(string? text, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Strict: optional sign and decimal point only; no thousands separators, exponents or culture formats.
        var trimmed = text.Trim();
        if (!decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || decimal.Round(value, DeviceConfigLimits.MaxDecimals) != value
            || Math.Abs(value) >= 1_000_000_000_000m)
        {
            errors[field] = [DeviceStrings.Code("InvalidNumber")];
            return null;
        }

        if (value <= 0)
        {
            errors[field] = [DeviceStrings.Code("MustBePositive")];
            return null;
        }

        return value;
    }

    private static JsonElement ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Invariant culture without trailing zeros (SQL Server pads stored decimals).</summary>
    private static string? Format(decimal? value) => value?.ToString("0.######", CultureInfo.InvariantCulture);

    private static DeviceConfigDto ToDto(DeviceConfig d) => new(
        d.Id,
        d.Name,
        d.AgentName,
        ParseObject(d.TransportJson),
        ParseObject(d.ProtocolJson),
        Format(d.Capacity),
        Format(d.Division),
        d.Unit,
        d.IsEnabled,
        DateTime.SpecifyKind(d.CreatedAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(d.UpdatedAt, DateTimeKind.Utc));

    [GeneratedRegex("^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell the agents that the device settings changed: {Error}")]
    private static partial void LogNotifyFailed(ILogger logger, string error);

    private sealed record Values(
        string Name,
        string? AgentName,
        string TransportJson,
        string ProtocolJson,
        decimal? Capacity,
        decimal? Division,
        string? Unit,
        bool IsEnabled)
    {
        public void ApplyTo(DeviceConfig device, DateTime now)
        {
            device.Name = Name;
            device.AgentName = AgentName;
            device.TransportJson = TransportJson;
            device.ProtocolJson = ProtocolJson;
            device.Capacity = Capacity;
            device.Division = Division;
            device.Unit = Unit;
            device.IsEnabled = IsEnabled;
            device.UpdatedAt = now;
        }
    }
}
