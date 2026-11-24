namespace ScalableVectorTemplates.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            return await CliApplication.RunAsync(args, Console.OpenStandardInput(), Console.OpenStandardOutput(),
                Console.OpenStandardError(), Environment.CurrentDirectory, cancellation.Token).ConfigureAwait(false);
        }
        finally { Console.CancelKeyPress -= handler; }
    }
}
