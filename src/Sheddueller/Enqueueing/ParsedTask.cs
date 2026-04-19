#pragma warning disable IDE0130

namespace Sheddueller;

internal sealed record ParsedTask(
  string MethodName,
  IReadOnlyList<string> MethodParameterTypeNames,
  IReadOnlyList<object?> SerializableArguments,
  IReadOnlyList<Type> SerializableParameterTypes);
