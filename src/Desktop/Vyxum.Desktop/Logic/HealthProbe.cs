namespace Vyxum.Desktop;

/// <summary>Polls the web application's <c>/health</c> endpoint until it answers.</summary>
public static class HealthProbe
{
    /// <summary>How often the endpoint is asked.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>How long the window waits before it gives up and offers "Try again".</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>True when <c>GET {baseUrl}/health</c> answered with a success status once; false after <paramref name="timeout"/>.</summary>
    public static async Task<bool> WaitAsync(
        HttpClient client, Uri baseUrl, TimeSpan interval, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var healthUrl = new Uri(baseUrl, "/health");
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsHealthyAsync(client, healthUrl, cancellationToken))
            {
                return true;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= timeout)
            {
                return false;
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private static async Task<bool> IsHealthyAsync(HttpClient client, Uri healthUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(healthUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The request itself timed out; the server is not ready yet.
            return false;
        }
    }
}
