using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>Lets <c>dotnet ef</c> create <see cref="PlatformDbContext"/> for either provider.</summary>
internal sealed class PlatformDesignTimeFactory : DesignTimeDbContextFactory<PlatformDbContext>;
