namespace Sheddueller;

internal static class TypeNameFormatter
{
    public static string Format(Type type)
    {
        return type.AssemblyQualifiedName
          ?? throw new InvalidOperationException($"Type '{type}' does not have an assembly-qualified name.");
    }

    public static Type Resolve(string typeName)
    {
        return Type.GetType(typeName, throwOnError: true)
          ?? throw new InvalidOperationException($"Could not resolve type '{typeName}'.");
    }
}
