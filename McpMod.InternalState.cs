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
            RecordInternalStateFailure(state, key, ex, context);
        }
    }

    // Adds the row-level reason and the top-level warning for a failed read. reasonKey
    // may be a dotted path into a nested object (vigor_attack.has_command); the reason
    // still goes to the row's unavailable_reasons so every failure is found in one place.
    private static void RecordInternalStateFailure(Dictionary<string, object?> state, string reasonKey,
        Exception ex, string context)
    {
        if (state.GetValueOrDefault("unavailable_reasons") is not Dictionary<string, string> reasons)
            state["unavailable_reasons"] = reasons = new Dictionary<string, string>();
        reasons[reasonKey] = $"{ex.GetType().Name}: {ex.Message}";
        Warn($"{context}.{reasonKey}", ex);
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

    private static T ReadDeclaredStateProperty<T>(object instance, Type declaringType, string name)
    {
        var property = declaringType.GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new MissingMemberException(declaringType.Name, name);
        if (property.PropertyType != typeof(T) || property.GetMethod == null || property.GetIndexParameters().Length != 0)
            throw new InvalidOperationException($"Expected readable {typeof(T).Name} property {declaringType.Name}.{name}");
        object? result;
        try { result = property.GetValue(instance); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new InvalidOperationException($"Cannot read {declaringType.Name}.{name}: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}", ex.InnerException);
        }
        if (result is not T value)
            throw new InvalidOperationException($"Expected non-null {typeof(T).Name} property {declaringType.Name}.{name}");
        return value;
    }
}
