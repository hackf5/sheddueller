namespace Sheddueller.Enqueueing;

internal sealed record ParsedJob(
  string MethodName,
  IReadOnlyList<string> MethodParameterTypeNames,
  IReadOnlyList<object?> SerializableArguments,
  IReadOnlyList<Type> SerializableParameterTypes);
