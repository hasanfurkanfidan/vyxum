namespace Vyxum.Platform.Abstractions;

/// <summary>A person known to a users module.</summary>
/// <param name="Id">Stable id of the person; never reused.</param>
/// <param name="DisplayName">The name to write on records, e.g. <c>Ayşe Demir</c>.</param>
public sealed record Actor(string Id, string DisplayName);

/// <summary>Who is working in the current request.</summary>
/// <remarks>
/// Without a users module (or while it is not licensed) nobody signs in and <see cref="Person"/> is null; modules
/// then leave person fields empty, as the free version does (decision V-03).
/// </remarks>
public interface ICurrentActor
{
    /// <summary>The signed-in person, or null when nobody is signed in or no users module is active.</summary>
    Actor? Person { get; }
}
