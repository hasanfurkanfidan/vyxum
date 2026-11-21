using Vyxum.Platform.Abstractions;

namespace Vyxum.Modules.Weighing;

/// <summary>Lets <c>dotnet ef</c> create <see cref="WeighingDbContext"/> for either provider.</summary>
internal sealed class WeighingDesignTimeFactory : DesignTimeDbContextFactory<WeighingDbContext>;
