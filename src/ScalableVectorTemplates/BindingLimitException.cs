namespace ScalableVectorTemplates;

internal sealed class BindingLimitException(string limit) : Exception("Binding budget exceeded.")
{
    internal string Limit { get; } = limit;
    internal static void Check(long value, int maximum, string limit)
    {
        if (value > maximum) throw new BindingLimitException(limit);
    }
    internal Diagnostic ToDiagnostic() => new("svt.limit.exceeded", DiagnosticSeverity.Error,
        Message, null, null, [], Limit);
}
