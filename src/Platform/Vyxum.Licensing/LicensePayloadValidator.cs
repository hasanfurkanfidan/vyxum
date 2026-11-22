namespace Vyxum.Licensing;

/// <summary>Required fields and ranges of a <see cref="LicensePayload"/>.</summary>
internal static class LicensePayloadValidator
{
    /// <summary>Returns an English description of the first problem, or <see langword="null"/> when the payload is valid.</summary>
    public static string? Validate(LicensePayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.LicenseId))
            return "licenseId is required.";
        if (!Enum.IsDefined(payload.Type))
            return "type is not a known license type.";
        if (payload.Licensee is null || string.IsNullOrWhiteSpace(payload.Licensee.Name))
            return "licensee.name is required.";
        if (payload.Modules is null)
            return "modules is required.";
        if (payload.Modules.Any(string.IsNullOrWhiteSpace))
            return "modules must not contain empty ids.";
        if (payload.Stations < 1)
            return "stations must be at least 1.";
        if (payload.Type == LicenseType.Perpetual && payload.ValidUntil is not null)
            return "validUntil must be empty for a perpetual license.";
        if (payload.Type != LicenseType.Perpetual && payload.ValidUntil is null)
            return "validUntil is required for a non-perpetual license.";
        if (payload.ValidUntil is { } until && until < payload.ValidFrom)
            return "validUntil must not be before validFrom.";
        if (payload.GraceDays is < 0 or > LicensePayload.MaxGraceDays)
            return $"graceDays must be between 0 and {LicensePayload.MaxGraceDays}.";
        return null;
    }

    /// <summary>Turns the raw JSON shape into a validated payload, or returns the problem.</summary>
    public static LicensePayload? FromRaw(LicenseJson.RawPayload raw, out string? problem)
    {
        problem = raw switch
        {
            { LicenseId: null } => "licenseId is required.",
            { Type: null } => "type is required.",
            { Licensee: null } => "licensee is required.",
            { Modules: null } => "modules is required.",
            { Stations: null } => "stations is required.",
            { IssuedAt: null } => "issuedAt is required.",
            { ValidFrom: null } => "validFrom is required.",
            _ => null,
        };
        if (problem is not null)
            return null;

        var payload = new LicensePayload
        {
            LicenseId = raw.LicenseId!,
            Type = raw.Type!.Value,
            Licensee = new Licensee { Name = raw.Licensee!.Name!, TaxNumber = raw.Licensee.TaxNumber },
            Modules = raw.Modules!.Select(m => m!).ToArray(),
            Stations = raw.Stations!.Value,
            IssuedAt = raw.IssuedAt!.Value,
            ValidFrom = raw.ValidFrom!.Value,
            ValidUntil = raw.ValidUntil,
            GraceDays = raw.GraceDays ?? LicensePayload.DefaultGraceDays,
            MaintenanceUntil = raw.MaintenanceUntil,
            ResellerId = raw.ResellerId,
        };
        problem = Validate(payload);
        return problem is null ? payload : null;
    }
}
