using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Threading;
using System.Linq;

namespace NinjaTrader.Custom.CBASTerminal
{
    public static class CBASTerminalUtils
    {
        // Run on the target's dispatcher if it is a DispatcherObject; otherwise run inline
        private static void RunOnTargetDispatcher(object target, Action action)
        {
            var dispObj = target as System.Windows.Threading.DispatcherObject;
            var d = dispObj?.Dispatcher;
            if (d == null)
            {
                action();
                return;
            }

            if (d.CheckAccess())
                action();
            else
                d.Invoke(action);
        }

        // Public entry: parse and execute a simple terminal command against 'target'
        // Supports:
        //   toggle <PropertyName>
        //   set <PropertyName> <Value>
        //   refresh
        // You can supply an optional logger and a forceRefresh action (e.g., to invalidate a chart).
        public static void HandleTerminalCommand(
            object target,
            string command,
            Action<string> log = null,
            Action forceRefresh = null)
        {
            if (target == null || string.IsNullOrWhiteSpace(command))
                return;

            try
            {
                var parts = command.Trim().Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return;

                var verb = parts[0].ToLowerInvariant();
                if (verb == "toggle" && parts.Length >= 2)
                {
                    var prop = parts[1];
                    bool ok = ToggleBoolProperty(target, prop);
        log?.Invoke(ok? $"Toggled {prop} on {target.GetType().Name}" :
                                     $"Toggle failed: property '{prop}' not found or not bool.");
                    if (ok) forceRefresh?.Invoke();
                    return;
                }

                if (verb == "set" && parts.Length >= 3)
                {
                    var prop = parts[1];
                    var value = parts[2]; bool ok = SetPropertyValue(target, prop, value, out string err);
    log?.Invoke(ok? $"Set {prop} = {value} on {target.GetType().Name}" :
                                     $"Set failed: {err}");
                    if (ok) forceRefresh?.Invoke();
                    return;
                }

if (verb == "refresh")
{
    forceRefresh?.Invoke();
    log?.Invoke("Refreshed.");
    return;
}

log?.Invoke($"Unknown command: '{command}'. Expected: toggle <prop> | set <prop> <value> | refresh");
            }
            catch (Exception ex)
            {
                log?.Invoke("HandleTerminalCommand error: " + ex.Message);
            }
        }

        // Toggle a bool (or bool?) property by name via reflection
        public static bool ToggleBoolProperty(object target, string propertyName)
{
    if (target == null || string.IsNullOrWhiteSpace(propertyName))
        return false;

    var prop = FindWritableProperty(target.GetType(), propertyName);
    if (prop == null)
        return false;

    var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
    if (propType != typeof(bool))
        return false;

    bool toggled = false;

    RunOnTargetDispatcher(target, () =>
    {
        var current = prop.GetValue(target);
        bool currentBool = current is bool b && b;

        object newValue;
        if (prop.PropertyType == typeof(bool))
            newValue = !currentBool;
        else
            newValue = (bool?)(!currentBool); // bool?

        prop.SetValue(target, newValue);
        toggled = true;
    });

    return toggled;
}

// Set a property to a value string; supports enums, numerics, bool, TimeSpan, DateTime and TypeConverters
public static bool SetPropertyValue(object target, string propertyName, string value, out string error)
{
    error = null;

    if (target == null || string.IsNullOrWhiteSpace(propertyName))
    {
        error = "Target or property name is null.";
        return false;
    }

    var prop = FindWritableProperty(target.GetType(), propertyName);
    if (prop == null)
    {
        error = $"Property '{propertyName}' not found or not writable.";
        return false;
    }

    try
    {
        object converted = ConvertStringToType(value, prop.PropertyType);

        RunOnTargetDispatcher(target, () =>
        {
            prop.SetValue(target, converted);
        });

        return true;
    }
    catch (Exception ex)
    {
        error = ex.Message;
        return false;
    }
}

// Force a refresh via a provided action (kept for API symmetry)
public static void ForceRefresh(Action refreshAction)
{
    try { refreshAction?.Invoke(); } catch { }
}

// --------- helpers ---------

private static PropertyInfo FindWritableProperty(Type type, string name)
{
    // Try exact name, then case-insensitive
    var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
    if (prop == null)
    {
        prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
    }
    if (prop != null && prop.CanWrite)
        return prop;

    return null;
}

private static object ConvertStringToType(string raw, Type targetType)
{
    if (targetType == typeof(string))
        return raw;

    var underlying = Nullable.GetUnderlyingType(targetType);
    bool isNullable = underlying != null;
    var effectiveType = underlying ?? targetType;

    if (string.IsNullOrWhiteSpace(raw))
    {
        if (isNullable)
            return null;
        throw new InvalidOperationException("Value cannot be empty.");
    }

    // Enums
    if (effectiveType.IsEnum)
        return Enum.Parse(effectiveType, raw, ignoreCase: true);

    // Booleans
    if (effectiveType == typeof(bool))
    {
        if (bool.TryParse(raw, out var b))
            return isNullable ? (bool?)b : b;
        // allow 0/1
        if (raw == "0" || raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return isNullable ? (bool?)false : false;
        if (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return isNullable ? (bool?)true : true;
        throw new InvalidOperationException($"Cannot parse '{raw}' as bool.");
    }

    // Numeric types
    if (effectiveType == typeof(int)) return isNullable ? (int?)int.Parse(raw, CultureInfo.InvariantCulture) : int.Parse(raw, CultureInfo.InvariantCulture);
    if (effectiveType == typeof(long)) return isNullable ? (long?)long.Parse(raw, CultureInfo.InvariantCulture) : long.Parse(raw, CultureInfo.InvariantCulture);
    if (effectiveType == typeof(double)) return isNullable ? (double?)double.Parse(raw, CultureInfo.InvariantCulture) : double.Parse(raw, CultureInfo.InvariantCulture);
    if (effectiveType == typeof(float)) return isNullable ? (float?)float.Parse(raw, CultureInfo.InvariantCulture) : float.Parse(raw, CultureInfo.InvariantCulture);
    if (effectiveType == typeof(decimal)) return isNullable ? (decimal?)decimal.Parse(raw, CultureInfo.InvariantCulture) : decimal.Parse(raw, CultureInfo.InvariantCulture);
    if (effectiveType == typeof(short)) return isNullable ? (short?)short.Parse(raw, CultureInfo.InvariantCulture) : short.Parse(raw, CultureInfo.InvariantCulture);
    if (effectiveType == typeof(byte)) return isNullable ? (byte?)byte.Parse(raw, CultureInfo.InvariantCulture) : byte.Parse(raw, CultureInfo.InvariantCulture);

    // Date/TimeSpan
    if (effectiveType == typeof(DateTime))
        return isNullable ? (DateTime?)DateTime.Parse(raw, CultureInfo.InvariantCulture) : DateTime.Parse(raw, CultureInfo.InvariantCulture);
    if (effectiveType == typeof(TimeSpan))
        return isNullable ? (TimeSpan?)TimeSpan.Parse(raw, CultureInfo.InvariantCulture) : TimeSpan.Parse(raw, CultureInfo.InvariantCulture);

    // Use a TypeConverter if available
    var converter = TypeDescriptor.GetConverter(effectiveType);
    if (converter != null && converter.CanConvertFrom(typeof(string)))
    {
        var converted = converter.ConvertFrom(null, CultureInfo.InvariantCulture, raw);
        if (isNullable)
            return converted;
        return converted ?? Activator.CreateInstance(effectiveType);
    }

    throw new InvalidOperationException($"No converter for type {effectiveType.Name}.");
}
    }
}
