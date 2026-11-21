using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vyxum.Licensing;

/// <summary>JSON shapes and settings of the <c>.vxlic</c> format.</summary>
internal static class LicenseJson
{
    public const string FormatV1 = "vxlic/1";

    /// <summary>The largest accepted file, in characters (the limit of a <c>plt_Settings</c> value).</summary>
    public const int MaxLength = 4000;

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        NumberHandling = JsonNumberHandling.Strict,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>The envelope as written to the file.</summary>
    internal sealed class Envelope
    {
        public string? Format { get; set; }
        public string? KeyId { get; set; }
        public string? Payload { get; set; }
        public string? Signature { get; set; }
    }

    /// <summary>The payload as read, before validation: every field is optional here.</summary>
    internal sealed class RawPayload
    {
        public string? LicenseId { get; set; }
        public LicenseType? Type { get; set; }
        public RawLicensee? Licensee { get; set; }
        public List<string?>? Modules { get; set; }
        public int? Stations { get; set; }
        public DateTimeOffset? IssuedAt { get; set; }
        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidUntil { get; set; }
        public int? GraceDays { get; set; }
        public DateOnly? MaintenanceUntil { get; set; }
        public string? ResellerId { get; set; }
    }

    internal sealed class RawLicensee
    {
        public string? Name { get; set; }
        public string? TaxNumber { get; set; }
    }
}
