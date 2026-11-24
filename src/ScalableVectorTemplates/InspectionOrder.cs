namespace ScalableVectorTemplates;

internal static class InspectionOrder
{
    internal static void Sort<T>(List<T> items, Comparison<T> comparison, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            items.Sort((left, right) =>
            {
                token.ThrowIfCancellationRequested();
                return comparison(left, right);
            });
        }
        catch (InvalidOperationException error) when (error.InnerException is OperationCanceledException && token.IsCancellationRequested)
        {
            // List.Sort wraps comparer exceptions; preserve the public cancellation contract.
            throw new OperationCanceledException(token);
        }
        token.ThrowIfCancellationRequested();
    }
}
