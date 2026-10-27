using System.Reflection;
using System.Text;
using Nefarius.ViGEm.Client;

namespace JoyNect.Core.Diagnostics;

/// <summary>
/// Prints the public surface of the ViGEm client library.
/// </summary>
/// <remarks>
/// This exists because the driver and its client library are end-of-life: if a future Windows build
/// changes either, the first useful question is "what does this build actually expose?". The output
/// is also what JoyNect's pad backend was written against, so it doubles as documentation.
/// </remarks>
public static class ApiSurfaceDumper
{
    private static readonly string[] InterestingTypeNames =
    [
        "ViGEmClient",
        "Xbox360Controller",
        "Xbox360Report",
        "Xbox360Button",
        "Xbox360Axis",
        "Xbox360Slider",
        "Xbox360FeedbackReceivedEventArgs",
        "ViGEmError",
    ];

    public static string Dump()
    {
        var builder = new StringBuilder();
        var assembly = typeof(ViGEmClient).Assembly;

        builder.AppendLine($"assembly  : {assembly.GetName().Name} {assembly.GetName().Version}");
        builder.AppendLine($"location  : {TryGetLocation(assembly)}");
        builder.AppendLine();

        foreach (var typeName in InterestingTypeNames)
        {
            var type = assembly.GetTypes()
                .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));

            if (type is null)
            {
                builder.AppendLine($"{typeName}: NOT PRESENT in this build");
                builder.AppendLine();
                continue;
            }

            builder.AppendLine($"{type.FullName}   [{(type.IsEnum ? "enum" : type.IsInterface ? "interface" : "class")}]");

            if (type.IsEnum)
            {
                foreach (var name in Enum.GetNames(type))
                {
                    builder.AppendLine($"    {name}");
                }

                builder.AppendLine();
                continue;
            }

            foreach (var member in DescribeMembers(type))
            {
                builder.AppendLine($"    {member}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IEnumerable<string> DescribeMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        foreach (var constructor in type.GetConstructors(flags))
        {
            yield return $"ctor({DescribeParameters(constructor.GetParameters())})";
        }

        foreach (var property in type.GetProperties(flags).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var accessors = $"{(property.CanRead ? "get" : string.Empty)}{(property.CanWrite ? "/set" : string.Empty)}";
            yield return $"prop {Short(property.PropertyType)} {property.Name} {{{accessors}}}";
        }

        foreach (var method in type.GetMethods(flags)
                     .Where(m => !m.IsSpecialName)
                     .OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            yield return $"func {Short(method.ReturnType)} {method.Name}({DescribeParameters(method.GetParameters())})";
        }
    }

    private static string DescribeParameters(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(p => $"{Short(p.ParameterType)} {p.Name}"));

    private static string Short(Type type) =>
        type.IsGenericType
            ? $"{type.Name.Split('`')[0]}<{string.Join(", ", type.GetGenericArguments().Select(Short))}>"
            : type.Name;

    private static string TryGetLocation(Assembly assembly)
    {
        try
        {
            return assembly.Location;
        }
        catch (Exception)
        {
            return "(not available)";
        }
    }
}
