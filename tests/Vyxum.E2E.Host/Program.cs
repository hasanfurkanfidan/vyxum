namespace Vyxum.E2E.Host;

internal static class EntryPoint
{
    // The sample paid module stands in for the paid modules, so that locking, previews and license states can be
    // checked with the open core alone. The paid modules repository runs E2EServer with its own modules.
    private static Task<int> Main(string[] args) => E2EServer.RunAsync(args, ["Vyxum.Samples.Paid"]);
}
