using System;
using System.Linq;
using System.Reflection;
using Il2Cpp;

internal static class Program
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Static | BindingFlags.Instance;

    private static readonly string[] SearchTerms =
    {
        "Encount", "Encounter", "Battle", "Start", "Exec", "Calc",
        "Step", "Rate", "Table", "Level"
    };

    private static void Main()
    {
        Console.WriteLine("SDF_PADMAP: " + string.Join(", ",
            Enum.GetNames(typeof(Il2Cpplibsdf_H.SDF_PADMAP))));
        DumpType(typeof(nbEncount));
        DumpType(typeof(fldProcess));
        DumpType(typeof(fldEnc));
        DumpType(typeof(fldTest));

        Console.WriteLine("\n=== Encounter-related types and declared members ===");
        foreach (Type type in typeof(nbEncount).Assembly.GetTypes()
                     .Where(IsRelevantType)
                     .OrderBy(type => type.FullName))
        {
            DumpType(type);
        }

        Console.WriteLine("\n=== All declared methods containing Encount/Encounter ===");
        foreach (Type type in typeof(nbEncount).Assembly.GetTypes().OrderBy(type => type.FullName))
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(All | BindingFlags.DeclaredOnly)
                    .Where(method => method.Name.Contains("Encount", StringComparison.OrdinalIgnoreCase) ||
                                     method.Name.Contains("Encounter", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (MethodInfo method in methods)
            {
                string parameters = string.Join(", ", method.GetParameters()
                    .Select(parameter => $"{parameter.ParameterType.FullName} {parameter.Name}"));
                Console.WriteLine($"{type.FullName}.{method.Name}({parameters}) -> {method.ReturnType.FullName}");
            }
        }
    }

    private static bool IsRelevantType(Type type)
    {
        string name = type.FullName ?? type.Name;
        return name.Contains("Encount", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Encounter", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("nbMain", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Battle", StringComparison.OrdinalIgnoreCase);
    }

    private static void DumpType(Type type)
    {
        Console.WriteLine($"\n--- TYPE {type.FullName} ---");
        foreach (FieldInfo field in type.GetFields(All | BindingFlags.DeclaredOnly)
                     .OrderBy(field => field.Name))
        {
            Console.WriteLine($"FIELD {Access(field)} {(field.IsStatic ? "static " : string.Empty)}{field.FieldType.FullName} {field.Name}");
        }

        foreach (PropertyInfo property in type.GetProperties(All | BindingFlags.DeclaredOnly)
                     .OrderBy(property => property.Name))
        {
            Console.WriteLine($"PROPERTY {property.PropertyType.FullName} {property.Name}");
        }

        foreach (MethodInfo method in type.GetMethods(All | BindingFlags.DeclaredOnly)
                     .Where(method => !method.IsSpecialName)
                     .OrderBy(method => method.Name))
        {
            string parameters = string.Join(", ", method.GetParameters()
                .Select(parameter => $"{parameter.ParameterType.FullName} {parameter.Name}"));
            Console.WriteLine($"METHOD {Access(method)} {(method.IsStatic ? "static " : string.Empty)}{method.ReturnType.FullName} {method.Name}({parameters})");
        }

        foreach (FieldInfo field in type.GetFields(All | BindingFlags.DeclaredOnly)
                     .Where(field => field.Name.StartsWith("NativeMethodInfoPtr_", StringComparison.Ordinal))
                     .OrderBy(field => field.Name))
        {
            Console.WriteLine($"NATIVE {field.Name}");
        }
    }

    private static string Access(FieldInfo field) => field.IsPublic ? "public" :
        field.IsPrivate ? "private" : "non-public";

    private static string Access(MethodInfo method) => method.IsPublic ? "public" :
        method.IsPrivate ? "private" : "non-public";
}
