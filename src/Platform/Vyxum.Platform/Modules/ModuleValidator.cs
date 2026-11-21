using System.Text.RegularExpressions;
using Vyxum.Platform.Abstractions;

namespace Vyxum.Platform;

/// <summary>
/// Checks that a set of modules forms a valid application: ids, capabilities, dependency
/// cycles and integration event names. All problems are collected and reported together.
/// </summary>
public static partial class ModuleValidator
{
    /// <summary>Throws <see cref="ModuleValidationException"/> listing every problem found.</summary>
    public static void Validate(IReadOnlyList<ModuleDescriptor> modules)
    {
        var problems = FindProblems(modules);
        if (problems.Count > 0)
        {
            throw new ModuleValidationException(problems);
        }
    }

    /// <summary>Returns every problem found; empty when the modules are valid.</summary>
    public static IReadOnlyList<string> FindProblems(IReadOnlyList<ModuleDescriptor> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var problems = new List<string>();
        CheckIds(modules, problems);
        CheckCapabilities(modules, problems);
        CheckCycles(modules, problems);
        CheckEvents(modules, problems);
        return problems;
    }

    /// <summary>True when <paramref name="id"/> is lowercase, dot-separated kebab-case, e.g. <c>samples.order-placed</c>.</summary>
    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)*$")]
    private static partial Regex IdPattern();

    private static void CheckIds(IReadOnlyList<ModuleDescriptor> modules, List<string> problems)
    {
        foreach (var module in modules.Where(m => !IsValidId(m.Manifest.Id)))
        {
            problems.Add($"Module type '{module.Module.GetType().FullName}' has an invalid id '{module.Manifest.Id}'. " +
                         "Use lowercase, dot-separated kebab-case, e.g. 'samples.orders'.");
        }

        foreach (var group in modules.GroupBy(m => m.Manifest.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add($"Module id '{group.Key}' is used by more than one module: " +
                         string.Join(", ", group.Select(m => m.Module.GetType().FullName)) + ".");
        }
    }

    private static void CheckCapabilities(IReadOnlyList<ModuleDescriptor> modules, List<string> problems)
    {
        foreach (var module in modules)
        {
            var manifest = module.Manifest;
            var invalid = manifest.Provides.Concat(manifest.Requires).Concat(manifest.Optional)
                .Where(c => !IsValidId(c.Id))
                .Distinct();
            foreach (var capability in invalid)
            {
                problems.Add($"Module '{manifest.Id}' declares an invalid capability id '{capability}'.");
            }
        }

        var providers = ProvidersByCapability(modules);
        foreach (var (capability, providerIds) in providers.Where(p => p.Value.Count > 1))
        {
            problems.Add($"Capability '{capability}' is provided by more than one module: " +
                         string.Join(", ", providerIds.Select(id => $"'{id}'")) + ".");
        }

        foreach (var module in modules)
        {
            foreach (var capability in module.Manifest.Requires.Distinct().Where(c => !providers.ContainsKey(c)))
            {
                problems.Add($"Module '{module.Manifest.Id}' requires capability '{capability}', but no module provides it.");
            }
        }
    }

    private static void CheckCycles(IReadOnlyList<ModuleDescriptor> modules, List<string> problems)
    {
        // Edge A -> B when A requires a capability that B provides.
        var providers = ProvidersByCapability(modules);
        var edges = modules
            .GroupBy(m => m.Manifest.Id, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(m => m.Manifest.Requires)
                    .Where(providers.ContainsKey)
                    .SelectMany(c => providers[c])
                    .Where(target => target != g.Key)
                    .Distinct()
                    .Order(StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        var reported = new HashSet<string>(StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        void Visit(string node)
        {
            if (done.Contains(node))
            {
                return;
            }

            var index = path.IndexOf(node);
            if (index >= 0)
            {
                var cycle = path.Skip(index).ToList();
                var start = cycle.IndexOf(cycle.Min(StringComparer.Ordinal)!);
                var normalized = cycle.Skip(start).Concat(cycle.Take(start)).ToList();
                normalized.Add(normalized[0]);
                var text = string.Join(" -> ", normalized);
                if (reported.Add(text))
                {
                    problems.Add($"Modules require each other's capabilities in a cycle: {text}.");
                }

                return;
            }

            path.Add(node);
            foreach (var target in edges[node])
            {
                Visit(target);
            }

            path.RemoveAt(path.Count - 1);
            done.Add(node);
        }

        foreach (var node in edges.Keys.Order(StringComparer.Ordinal))
        {
            Visit(node);
        }
    }

    private static void CheckEvents(IReadOnlyList<ModuleDescriptor> modules, List<string> problems)
    {
        var published = new List<(string ModuleId, Type EventType, IntegrationEventAttribute Event)>();

        foreach (var module in modules)
        {
            foreach (var eventType in module.Manifest.Publishes.Distinct())
            {
                var attribute = CheckEventType(module.Manifest.Id, eventType, problems);
                if (attribute is not null)
                {
                    published.Add((module.Manifest.Id, eventType, attribute));
                }
            }
        }

        foreach (var group in published.GroupBy(p => (p.Event.Name, p.Event.Version)))
        {
            var moduleIds = group.Select(p => p.ModuleId).Distinct().ToList();
            if (moduleIds.Count > 1)
            {
                problems.Add($"Integration event '{group.Key.Name}' v{group.Key.Version} is published by more than one module: " +
                             string.Join(", ", moduleIds.Select(id => $"'{id}'")) + ".");
            }
            else if (group.Count() > 1)
            {
                problems.Add($"Module '{moduleIds[0]}' uses integration event '{group.Key.Name}' v{group.Key.Version} on more than one type: " +
                             string.Join(", ", group.Select(p => p.EventType.FullName)) + ".");
            }
        }
    }

    private static IntegrationEventAttribute? CheckEventType(string moduleId, Type eventType, List<string> problems)
    {
        if (!typeof(IIntegrationEvent).IsAssignableFrom(eventType))
        {
            problems.Add($"Module '{moduleId}' lists '{eventType.FullName}' as an event, but it does not implement IIntegrationEvent.");
            return null;
        }

        var attribute = IntegrationEventAttribute.Find(eventType);
        if (attribute is null)
        {
            problems.Add($"Integration event type '{eventType.FullName}' (module '{moduleId}') has no [IntegrationEvent] attribute.");
            return null;
        }

        if (!IsValidId(attribute.Name) || attribute.Version < 1)
        {
            problems.Add($"Integration event type '{eventType.FullName}' (module '{moduleId}') has an invalid name or version " +
                         $"'{attribute}'. Use a kebab-case name such as 'samples.order-placed' and a version of 1 or more.");
            return null;
        }

        return attribute;
    }

    private static Dictionary<Capability, List<string>> ProvidersByCapability(IReadOnlyList<ModuleDescriptor> modules) =>
        modules
            .SelectMany(m => m.Manifest.Provides.Distinct().Select(c => (Capability: c, ModuleId: m.Manifest.Id)))
            .Where(p => IsValidId(p.Capability.Id))
            .GroupBy(p => p.Capability)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ModuleId).ToList());
}
