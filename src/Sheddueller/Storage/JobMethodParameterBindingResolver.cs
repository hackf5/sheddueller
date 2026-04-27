namespace Sheddueller.Storage;

internal static class JobMethodParameterBindingResolver
{
    public static IReadOnlyList<JobMethodParameterBinding> Normalize(
        IReadOnlyList<Type> methodParameterTypes,
        IReadOnlyList<JobMethodParameterBinding>? parameterBindings)
    {
        ArgumentNullException.ThrowIfNull(methodParameterTypes);

        if (parameterBindings is { Count: > 0 })
        {
            return parameterBindings;
        }

        var inferred = new JobMethodParameterBinding[methodParameterTypes.Count];
        for (var i = 0; i < methodParameterTypes.Count; i++)
        {
            inferred[i] = CreateInferredBinding(methodParameterTypes[i]);
        }

        return inferred;
    }

    public static IReadOnlyList<JobMethodParameterBinding> Normalize(
        IReadOnlyList<string> methodParameterTypes,
        IReadOnlyList<JobMethodParameterBinding>? parameterBindings)
    {
        ArgumentNullException.ThrowIfNull(methodParameterTypes);

        if (parameterBindings is { Count: > 0 })
        {
            return parameterBindings;
        }

        var inferred = new JobMethodParameterBinding[methodParameterTypes.Count];
        for (var i = 0; i < methodParameterTypes.Count; i++)
        {
            inferred[i] = CreateInferredBinding(methodParameterTypes[i]);
        }

        return inferred;
    }

    private static JobMethodParameterBinding CreateInferredBinding(Type parameterType)
      => parameterType switch
      {
          Type type when type == typeof(CancellationToken) => new JobMethodParameterBinding(JobMethodParameterBindingKind.CancellationToken),
          Type type when type == typeof(IJobContext) => new JobMethodParameterBinding(JobMethodParameterBindingKind.JobContext),
          Type type when type == typeof(IProgress<decimal>) => new JobMethodParameterBinding(JobMethodParameterBindingKind.ProgressReporter),
          _ => new JobMethodParameterBinding(JobMethodParameterBindingKind.Serialized),
      };

    private static JobMethodParameterBinding CreateInferredBinding(string parameterType)
      => string.Equals(parameterType, typeof(CancellationToken).AssemblyQualifiedName, StringComparison.Ordinal)
        ? new JobMethodParameterBinding(JobMethodParameterBindingKind.CancellationToken)
        : string.Equals(parameterType, typeof(IJobContext).AssemblyQualifiedName, StringComparison.Ordinal)
          ? new JobMethodParameterBinding(JobMethodParameterBindingKind.JobContext)
          : string.Equals(parameterType, typeof(IProgress<decimal>).AssemblyQualifiedName, StringComparison.Ordinal)
            ? new JobMethodParameterBinding(JobMethodParameterBindingKind.ProgressReporter)
            : new JobMethodParameterBinding(JobMethodParameterBindingKind.Serialized);
}
