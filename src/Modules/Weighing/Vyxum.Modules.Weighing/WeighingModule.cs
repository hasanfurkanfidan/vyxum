using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Weighing;

/// <summary>Weighing records: live weight from a scale, tare and net, amount, cancel, list and totals.</summary>
public sealed class WeighingModule : IModule
{
    /// <summary>Capability this module provides.</summary>
    public const string RecordsCapability = "weighing.records";

    /// <summary>Optional: the product catalog. Without it records are product-less.</summary>
    public const string ProductsCapability = "products.catalog";

    /// <summary>Optional: label printing. Shows "Save and print label" on the screen.</summary>
    public const string LabelsCapability = "labels.print";

    /// <summary>Optional: users. Shows the operator column and filter.</summary>
    public const string UsersCapability = "identity.users";

    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        Id = "weighing",
        DisplayNameKey = WeighingStrings.KeyPrefix + ".ModuleName",
        Version = new Version(1, 0),
        Provides = [RecordsCapability],
        Optional = [ProductsCapability, LabelsCapability, UsersCapability],
        Publishes = [typeof(WeighingRecorded), typeof(WeighingCancelled)],
    };

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.AddDbContext<WeighingDbContext>();
        context.AddEventHandler<ProductSaved, ProductSavedHandler>();
        services.AddOptions<WeighingOptions>()
            .Bind(context.Configuration.GetSection(WeighingOptions.SectionName))
            .Validate(o => o.MinimumNet >= 0, "Vyxum:Weighing:MinimumNet cannot be negative.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Currency) && o.Currency.Trim().Length <= WeighingLimits.CurrencyMaxLength,
                $"Vyxum:Weighing:Currency must be 1 to {WeighingLimits.CurrencyMaxLength} characters.")
            .Validate(o => o.AmountDecimals is >= 0 and <= WeighingLimits.AmountMaxDecimals,
                $"Vyxum:Weighing:AmountDecimals must be between 0 and {WeighingLimits.AmountMaxDecimals}.")
            .Validate(o => Enum.IsDefined(o.AmountRounding), "Vyxum:Weighing:AmountRounding is not a known rounding mode.")
            .ValidateOnStart();
        services.AddScoped<WeighingService>();
        services.AddScoped<WeighingQueries>();
        services.AddScoped<WeighingExporter>();
        services.AddUiStrings(WeighingStrings.KeyPrefix, WeighingStrings.ResourceManager);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => WeighingEndpoints.Map(endpoints);
}
