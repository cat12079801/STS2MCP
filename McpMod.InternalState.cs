using System;
using System.Collections.Generic;
using System.Reflection;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void ReadInternalStateValue(Dictionary<string, object?> state, string key,
        Func<object?> read, string context)
    {
        try { state[key] = read(); }
        catch (Exception ex)
        {
            state[key] = null;
            if (state.GetValueOrDefault("unavailable_reasons") is not Dictionary<string, string> reasons)
                state["unavailable_reasons"] = reasons = new Dictionary<string, string>();
            reasons[key] = $"{ex.GetType().Name}: {ex.Message}";
            Warn($"{context}.{key}", ex);
        }
    }

    private static T ReadDeclaredStateField<T>(object instance, Type declaringType, string name)
    {
        var field = declaringType.GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new MissingFieldException(declaringType.Name, name);
        if (field.FieldType != typeof(T) || field.GetValue(instance) is not T value)
            throw new InvalidOperationException($"Expected non-null {typeof(T).Name} field {declaringType.Name}.{name}");
        return value;
    }
}
