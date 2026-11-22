namespace Vyxum.Platform.Abstractions;

/// <summary>Answers whether the person in the current request may do something.</summary>
/// <remarks>
/// Without an active users module everything is allowed, as in the free version. Always check on the server; hiding a
/// button is only a convenience. A module answers a refused operation with 403 and an error code.
/// </remarks>
public interface IAuthorization
{
    /// <summary>True when the current person has <paramref name="permission"/>.</summary>
    bool IsAllowed(Permission permission);
}
