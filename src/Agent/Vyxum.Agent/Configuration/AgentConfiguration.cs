using Microsoft.Extensions.Configuration;
using Vyxum.Devices;
using Vyxum.Devices.GenericAscii;
using Vyxum.Devices.MtSics;
using Vyxum.Devices.Simulator;
using Vyxum.Devices.ToledoContinuous;
using Vyxum.Devices.Transports;

namespace Vyxum.Agent.Configuration;

/// <summary>Thrown at startup when the agent settings are invalid. The message names every bad setting.</summary>
public sealed class AgentConfigurationException : Exception
{
    /// <summary>Creates the exception from a list of errors.</summary>
    public AgentConfigurationException(IReadOnlyList<string> errors, Exception? innerException = null)
        : base("The agent settings are invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "  - " + e)), innerException)
    {
        Errors = errors;
    }

    /// <summary>Creates the exception with a message.</summary>
    public AgentConfigurationException()
        : this(["Unknown error."])
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public AgentConfigurationException(string message)
        : this([message])
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    public AgentConfigurationException(string message, Exception innerException)
        : this([message], innerException)
    {
    }

    /// <summary>The individual errors, each naming the setting.</summary>
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>A configured device, ready to be run.</summary>
/// <param name="Id">The device id.</param>
/// <param name="TransportDescription">A short description of the transport, for logs.</param>
/// <param name="ProtocolName">The protocol type, for logs.</param>
/// <param name="CreateTransport">Creates the transport for one connection attempt.</param>
/// <param name="Protocol">The protocol.</param>
/// <param name="Simulator">For a simulator transport, the transport instance (to inject faults in tests); otherwise null.</param>
public sealed record AgentDevice(
    string Id,
    string TransportDescription,
    string ProtocolName,
    Func<ITransport> CreateTransport,
    IScaleProtocol Protocol,
    SimulatedTransport? Simulator);

/// <summary>Reads and checks the agent settings and turns them into devices.</summary>
public static class AgentConfiguration
{
    /// <summary>Transport type names.</summary>
    public static IReadOnlyList<string> TransportTypes { get; } = ["serial", "tcp", "simulator"];

    /// <summary>Protocol type names.</summary>
    public static IReadOnlyList<string> ProtocolTypes { get; } = ["mt-sics", "toledo-continuous", "generic-ascii"];

    /// <summary>Binds the <c>Vyxum:Agent</c> section. Throws <see cref="AgentConfigurationException"/> when a value has the wrong type.</summary>
    public static AgentSettings Bind(IConfiguration configuration)
    {
        var settings = new AgentSettings();
        BindInto(configuration, settings);
        return settings;
    }

    /// <summary>Binds the <c>Vyxum:Agent</c> section into <paramref name="settings"/>.</summary>
    public static void BindInto(IConfiguration configuration, AgentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(settings);
        var section = configuration.GetSection(AgentSettings.SectionName);
        try
        {
            section.Bind(settings);

            // The binder silently skips a list element that fails to convert, which would make a device disappear.
            // Binding each device on its own reports the bad value instead.
            settings.Devices.Clear();
            foreach (var device in section.GetSection(nameof(AgentSettings.Devices)).GetChildren())
            {
                settings.Devices.Add(device.Get<DeviceSettings>() ?? new DeviceSettings());
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new AgentConfigurationException([ex.Message], ex);
        }
    }

    /// <summary>
    /// Checks the settings and creates the devices. Throws <see cref="AgentConfigurationException"/> listing every
    /// invalid setting.
    /// </summary>
    public static IReadOnlyList<AgentDevice> CreateDevices(AgentSettings settings, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var time = timeProvider ?? TimeProvider.System;
        var errors = new List<string>();
        const string root = AgentSettings.SectionName;

        if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var serverUri) || serverUri.Scheme is not ("http" or "https"))
        {
            errors.Add($"{root}:ServerUrl: '{settings.ServerUrl}' is not an absolute http or https address.");
        }

        RequirePositive(errors, $"{root}:HeartbeatInterval", settings.HeartbeatInterval);
        RequirePositive(errors, $"{root}:ReconnectDelay", settings.ReconnectDelay);
        RequirePositive(errors, $"{root}:MaxReconnectDelay", settings.MaxReconnectDelay);

        if (settings.Devices.Count == 0)
        {
            errors.Add($"{root}:Devices: at least one device is required.");
        }

        var devices = new List<AgentDevice>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < settings.Devices.Count; i++)
        {
            var path = $"{root}:Devices:{i}";
            var device = settings.Devices[i];
            var id = device.Id?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                errors.Add($"{path}:Id: a device id is required.");
            }
            else if (!ids.Add(id))
            {
                errors.Add($"{path}:Id: the id '{id}' is used by more than one device.");
            }

            var transport = CreateTransport(errors, $"{path}:Transport", device.Transport);
            var protocol = CreateProtocol(errors, $"{path}:Protocol", device.Protocol, time);
            if (!string.IsNullOrEmpty(id) && transport is { } t && protocol is { } p)
            {
                devices.Add(new AgentDevice(id, t.Description, p.Name, t.Factory, p.Protocol, t.Simulator));
            }
        }

        if (errors.Count > 0)
        {
            throw new AgentConfigurationException(errors);
        }

        return devices;
    }

    private static (string Description, Func<ITransport> Factory, SimulatedTransport? Simulator)? CreateTransport(
        List<string> errors, string path, TransportSettings? settings)
    {
        var type = settings?.Type?.Trim().ToLowerInvariant();
        if (settings is null || string.IsNullOrEmpty(type))
        {
            errors.Add($"{path}:Type: a transport type is required ({string.Join(", ", TransportTypes)}).");
            return null;
        }

        var count = errors.Count;
        try
        {
            switch (type)
            {
                case "serial":
                {
                    if (string.IsNullOrWhiteSpace(settings.PortName))
                    {
                        errors.Add($"{path}:PortName: a serial port name is required, for example COM3.");
                    }

                    if (settings.BaudRate <= 0)
                    {
                        errors.Add($"{path}:BaudRate: must be greater than zero.");
                    }

                    if (settings.DataBits is < 5 or > 8)
                    {
                        errors.Add($"{path}:DataBits: must be between 5 and 8.");
                    }

                    if (errors.Count > count)
                    {
                        return null;
                    }

                    var options = new SerialTransportOptions
                    {
                        PortName = settings.PortName!.Trim(),
                        BaudRate = settings.BaudRate,
                        DataBits = settings.DataBits,
                        Parity = settings.Parity,
                        StopBits = settings.StopBits,
                        Handshake = settings.Handshake,
                        DtrEnable = settings.DtrEnable,
                        RtsEnable = settings.RtsEnable,
                    };
                    var description = new SerialTransport(options).Description;
                    return (description, () => new SerialTransport(options), null);
                }

                case "tcp":
                {
                    if (string.IsNullOrWhiteSpace(settings.Host))
                    {
                        errors.Add($"{path}:Host: a host name or IP address is required.");
                    }

                    if (settings.Port is not (>= 1 and <= 65535))
                    {
                        errors.Add($"{path}:Port: a TCP port between 1 and 65535 is required.");
                    }

                    if (settings.ConnectTimeout is { } connectTimeout)
                    {
                        RequirePositive(errors, $"{path}:ConnectTimeout", connectTimeout);
                    }

                    if (settings.KeepAlive is { } keepAlive)
                    {
                        RequirePositive(errors, $"{path}:KeepAlive", keepAlive);
                    }

                    if (errors.Count > count)
                    {
                        return null;
                    }

                    var options = new TcpTransportOptions { Host = settings.Host!.Trim(), Port = settings.Port!.Value, KeepAlive = settings.KeepAlive };
                    if (settings.ConnectTimeout is { } timeout)
                    {
                        options = options with { ConnectTimeout = timeout };
                    }

                    return ($"{options.Host}:{options.Port}", () => new TcpTransport(options), null);
                }

                case "simulator":
                {
                    var sim = settings.Simulator ?? new SimulatorSettings();
                    if (sim.Interval is { } interval)
                    {
                        RequirePositive(errors, $"{path}:Simulator:Interval", interval);
                    }

                    if (sim.Kind == SimulatorKind.Replay && string.IsNullOrWhiteSpace(sim.RecordingPath))
                    {
                        errors.Add($"{path}:Simulator:RecordingPath: a recording file is required for a replay simulator.");
                    }

                    if (sim.Kind == SimulatorKind.Replay && !string.IsNullOrWhiteSpace(sim.RecordingPath) && !File.Exists(sim.RecordingPath))
                    {
                        errors.Add($"{path}:Simulator:RecordingPath: the file '{sim.RecordingPath}' does not exist.");
                    }

                    if (errors.Count > count)
                    {
                        return null;
                    }

                    var options = new SimulatorOptions
                    {
                        Kind = sim.Kind,
                        Weight = sim.Weight,
                        Unit = sim.Unit,
                        State = sim.State,
                        Checksum = sim.Checksum,
                        RecordingPath = sim.RecordingPath,
                        ReplayMode = sim.ReplayMode,
                        Loop = sim.Loop,
                        FirstCommandReturnsSyntaxError = sim.FirstCommandReturnsSyntaxError,
                        Silent = sim.Silent,
                        Busy = sim.Busy,
                    };
                    if (!string.IsNullOrWhiteSpace(sim.SerialNumber))
                    {
                        options = options with { SerialNumber = sim.SerialNumber };
                    }

                    if (sim.Interval is { } simInterval)
                    {
                        options = options with { Interval = simInterval };
                    }

                    // One instance serves every connection attempt; each open starts a new device session.
                    var transport = SimulatorFactory.CreateTransport(options);
                    return (transport.Description, () => transport, transport);
                }

                default:
                    errors.Add($"{path}:Type: '{settings.Type}' is not a known transport type. Use one of: {string.Join(", ", TransportTypes)}.");
                    return null;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            errors.Add($"{path}: {ex.Message}");
            return null;
        }
    }

    private static (string Name, IScaleProtocol Protocol)? CreateProtocol(
        List<string> errors, string path, ProtocolSettings? settings, TimeProvider time)
    {
        var type = settings?.Type?.Trim().ToLowerInvariant();
        if (settings is null || string.IsNullOrEmpty(type))
        {
            errors.Add($"{path}:Type: a protocol type is required ({string.Join(", ", ProtocolTypes)}).");
            return null;
        }

        try
        {
            switch (type)
            {
                case "mt-sics":
                {
                    var command = string.IsNullOrWhiteSpace(settings.Command) ? "SI" : settings.Command.Trim().ToUpperInvariant();
                    if (!MtSicsProtocolOptions.AllowedCommands.Contains(command, StringComparer.Ordinal))
                    {
                        errors.Add(
                            $"{path}:Command: '{settings.Command}' is not allowed. Only read commands are sent to the scale: " +
                            $"{string.Join(", ", MtSicsProtocolOptions.AllowedCommands)}.");
                        return null;
                    }

                    var options = new MtSicsProtocolOptions { Command = command };
                    options = options with
                    {
                        PollInterval = Positive(errors, $"{path}:PollInterval", settings.PollInterval) ?? options.PollInterval,
                        ResponseTimeout = Positive(errors, $"{path}:ResponseTimeout", settings.ResponseTimeout) ?? options.ResponseTimeout,
                        RetriesOnError = AtLeast(errors, $"{path}:RetriesOnError", settings.RetriesOnError, 0) ?? options.RetriesOnError,
                        MaxConsecutiveTimeouts = AtLeast(errors, $"{path}:MaxConsecutiveTimeouts", settings.MaxConsecutiveTimeouts, 1) ?? options.MaxConsecutiveTimeouts,
                        LineTerminator = settings.Terminator ?? options.LineTerminator,
                    };
                    return ("mt-sics", new MtSicsProtocol(options, time));
                }

                case "toledo-continuous":
                {
                    var options = new ToledoContinuousOptions { ChecksumEnabled = settings.ChecksumEnabled };
                    options = options with
                    {
                        InactivityTimeout = Positive(errors, $"{path}:InactivityTimeout", settings.InactivityTimeout) ?? options.InactivityTimeout,
                        MaxConsecutiveInvalidFrames = AtLeast(errors, $"{path}:MaxConsecutiveInvalidFrames", settings.MaxConsecutiveInvalidFrames, 1) ?? options.MaxConsecutiveInvalidFrames,
                    };
                    return ("toledo-continuous", new ToledoContinuousProtocol(options, time));
                }

                case "generic-ascii":
                {
                    var defaults = new GenericAsciiOptions();
                    var options = new GenericAsciiOptions
                    {
                        Pattern = settings.Pattern ?? string.Empty,
                        DecimalSeparator = settings.DecimalSeparator ?? defaults.DecimalSeparator,
                        ThousandsSeparator = settings.ThousandsSeparator,
                        StablePattern = settings.StablePattern,
                        MotionPattern = settings.MotionPattern,
                        OverloadPattern = settings.OverloadPattern,
                        UnderloadPattern = settings.UnderloadPattern,
                        NetPattern = settings.NetPattern,
                        DefaultUnit = settings.DefaultUnit,
                        Terminator = settings.Terminator ?? defaults.Terminator,
                        RequestCommand = settings.RequestCommand,
                        RequestTerminator = settings.RequestTerminator ?? defaults.RequestTerminator,
                        PollInterval = settings.PollInterval ?? defaults.PollInterval,
                        ResponseTimeout = settings.ResponseTimeout ?? defaults.ResponseTimeout,
                        MaxConsecutiveTimeouts = settings.MaxConsecutiveTimeouts ?? defaults.MaxConsecutiveTimeouts,
                        InactivityTimeout = settings.InactivityTimeout ?? defaults.InactivityTimeout,
                        MaxLineLength = settings.MaxLineLength ?? defaults.MaxLineLength,
                    };
                    try
                    {
                        options.Validate();
                    }
                    catch (ArgumentException ex)
                    {
                        errors.Add($"{path}:{ex.ParamName}: {StripParamName(ex)}");
                        return null;
                    }

                    return ("generic-ascii", new GenericAsciiProtocol(options, time));
                }

                default:
                    errors.Add($"{path}:Type: '{settings.Type}' is not a known protocol type. Use one of: {string.Join(", ", ProtocolTypes)}.");
                    return null;
            }
        }
        catch (ArgumentException ex)
        {
            errors.Add($"{path}: {StripParamName(ex)}");
            return null;
        }
    }

    private static string StripParamName(ArgumentException ex)
    {
        var suffix = $" (Parameter '{ex.ParamName}')";
        return ex.ParamName is not null && ex.Message.EndsWith(suffix, StringComparison.Ordinal)
            ? ex.Message[..^suffix.Length]
            : ex.Message;
    }

    private static void RequirePositive(List<string> errors, string path, TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            errors.Add($"{path}: must be greater than zero.");
        }
    }

    private static TimeSpan? Positive(List<string> errors, string path, TimeSpan? value)
    {
        if (value is { } v && v <= TimeSpan.Zero)
        {
            errors.Add($"{path}: must be greater than zero.");
            return null;
        }

        return value;
    }

    private static int? AtLeast(List<string> errors, string path, int? value, int minimum)
    {
        if (value is { } v && v < minimum)
        {
            errors.Add($"{path}: must be at least {minimum}.");
            return null;
        }

        return value;
    }
}
