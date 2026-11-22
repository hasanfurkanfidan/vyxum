namespace Vyxum.Platform.Abstractions;

/// <summary>A permission a module declares, with what a users module needs to show and assign it.</summary>
public sealed class PermissionDefinition
{
    /// <summary>The permission.</summary>
    public required Permission Id { get; init; }

    /// <summary>
    /// Full UI string key (prefix included, e.g. <c>weighing.PermissionCancel</c>) of the permission's name, as a users
    /// screen lists it. The key must be in a string set the module registers itself; the platform checks it at startup.
    /// </summary>
    public required string NameKey { get; init; }

    /// <summary>
    /// Whether the operation needs an electronic signature (the person enters their password again). The module asks
    /// for it; a users module marks the permission on its screens.
    /// </summary>
    public bool RequiresSignature { get; init; }

    /// <summary>
    /// The built-in roles that have this permission by default, by id; see <see cref="BuiltInRoles"/>. A users module
    /// uses them for its ready-made roles; the customer can change them.
    /// </summary>
    public IReadOnlyList<string> DefaultRoles { get; init; } = [];
}

/// <summary>Ids of the ready-made roles a users module offers. Modules name them in <see cref="PermissionDefinition.DefaultRoles"/>.</summary>
public static class BuiltInRoles
{
    /// <summary>Works on the line: weighs, prints labels, weighs batches.</summary>
    public const string Operator = "operator";

    /// <summary>Works on the floor: approves exceptions, resets forgotten PINs.</summary>
    public const string ShiftSupervisor = "shift-supervisor";

    /// <summary>Approves recipes and batches, decides on non-conforming batches.</summary>
    public const string Quality = "quality";

    /// <summary>Writes recipes and sends them for approval.</summary>
    public const string RecipeAuthor = "recipe-author";

    /// <summary>Opens work orders, views records.</summary>
    public const string Planning = "planning";

    /// <summary>Manages people, devices and the license. Does not approve weighing or recipes.</summary>
    public const string Administrator = "administrator";

    /// <summary>All built-in role ids, in the order screens list them.</summary>
    public static IReadOnlyList<string> All { get; } = [Operator, ShiftSupervisor, Quality, RecipeAuthor, Planning, Administrator];
}
