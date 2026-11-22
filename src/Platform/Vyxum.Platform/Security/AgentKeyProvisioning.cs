using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace Vyxum.Platform;

/// <summary>
/// Gives a new installation its own agent key. The installer runs the web host once with <see cref="Argument"/>;
/// the key is written to <c>vyxum.json</c> for the web application (<see cref="ServerKey"/>) and the agent on the
/// same computer (<see cref="AgentKey"/>). With a key, a connection to the agent hub must prove it knows the key,
/// so being on this computer is no longer enough (decision V-26).
/// </summary>
public static class AgentKeyProvisioning
{
    /// <summary>Command line switch of the web host: write a key if there is none, then exit.</summary>
    public const string Argument = "--ensure-agent-key";

    /// <summary>The key the web application expects (see <see cref="AgentOptions.Key"/>).</summary>
    public const string ServerKey = AgentOptions.SectionName + ":Key";

    /// <summary>The key the agent sends (<c>Vyxum:Agent:Key</c>, read by the agent from the same file).</summary>
    public const string AgentKey = "Vyxum:Agent:Key";

    /// <summary>
    /// Writes a new random key for both sides when the web application has none, and gives an existing key to an
    /// agent that lacks it. A key that is already set is never changed, so agents on other computers keep working.
    /// </summary>
    /// <returns>True when the file was written.</returns>
    public static bool EnsureKey(VyxumSettingsFile file, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(configuration);

        var server = configuration[ServerKey];
        var agent = configuration[AgentKey];
        if (!string.IsNullOrWhiteSpace(server))
        {
            if (!string.IsNullOrWhiteSpace(agent))
            {
                return false;
            }

            file.Update(new Dictionary<string, string?> { [AgentKey] = server });
            return true;
        }

        var key = NewKey();
        file.Update(new Dictionary<string, string?> { [ServerKey] = key, [AgentKey] = key });
        return true;
    }

    /// <summary>A new key: 32 random bytes as URL-safe Base64 (43 characters).</summary>
    public static string NewKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
