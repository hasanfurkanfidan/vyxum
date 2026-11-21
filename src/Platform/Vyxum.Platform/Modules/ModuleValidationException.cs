namespace Vyxum.Platform;

/// <summary>Thrown at startup when the loaded modules do not form a valid application.</summary>
public sealed class ModuleValidationException : Exception
{
    /// <summary>Creates the exception from the list of problems found.</summary>
    public ModuleValidationException(IReadOnlyList<string> problems)
        : base(BuildMessage(problems))
    {
        Problems = problems;
    }

    /// <summary>Every problem found, one human-readable sentence each.</summary>
    public IReadOnlyList<string> Problems { get; }

    private static string BuildMessage(IReadOnlyList<string> problems) =>
        $"Module validation failed with {problems.Count} problem(s):" + Environment.NewLine +
        string.Join(Environment.NewLine, problems.Select(p => " - " + p));
}
