using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vyxum.Agent.Configuration;

namespace Vyxum.Agent;

/// <summary>Entry point of Vyxum Agent.</summary>
internal static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        IHost host;
        try
        {
            host = AgentHost.CreateBuilder(args).Build();
        }
        catch (AgentConfigurationException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }

        using (host)
        {
            try
            {
                await host.StartAsync().ConfigureAwait(false);
            }
            catch (AgentConfigurationException ex)
            {
                var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Vyxum.Agent");
                LogInvalidSettings(logger, ex.Message);
                return 1;
            }

            await host.WaitForShutdownAsync().ConfigureAwait(false);
            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Vyxum Agent cannot start. {Details}")]
    private static partial void LogInvalidSettings(ILogger logger, string details);
}
