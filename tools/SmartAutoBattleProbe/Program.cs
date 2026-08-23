using System;
using System.Linq;
using System.Reflection;
using Il2Cpp;

internal static class Program
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Static | BindingFlags.Instance;

    private static readonly string[] SpeedTerms =
    {
        "ai", "skill", "select", "target", "aisyo", "weak", "action",
        "form", "unit", "work", "cursor", "tar", "analy"
    };

    private static void Main()
    {
        Assembly assembly = typeof(nbEncount).Assembly;
        Console.WriteLine("Assembly: " + assembly.FullName);
        Console.WriteLine("SIPressType: " + string.Join(", ", Enum.GetNames(typeof(SIPressType))));

        string[] requestedTerms = Environment.GetCommandLineArgs().Skip(1).ToArray();
        string[] requestedTypes = requestedTerms
            .Where(term => term.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            .Select(term => term.Substring("type:".Length))
            .ToArray();
        if (requestedTypes.Length > 0)
        {
            foreach (Type requestedType in assembly.GetTypes()
                         .Where(type => requestedTypes.Any(name =>
                             (type.FullName ?? type.Name).Contains(name, StringComparison.OrdinalIgnoreCase)))
                         .OrderBy(type => type.FullName))
            {
                Console.WriteLine("\n--- " + requestedType.FullName + " ---");
                foreach (MemberInfo member in requestedType.GetMembers(All | BindingFlags.DeclaredOnly)
                             .Where(member => member is MethodInfo || member is PropertyInfo ||
                                              (member is FieldInfo field &&
                                               !field.Name.StartsWith("Native", StringComparison.Ordinal)))
                             .OrderBy(member => member.Name))
                {
                    Console.WriteLine(Describe(member));
                }
            }
            return;
        }
        if (requestedTerms.Length > 0)
        {
            foreach (Type requestedType in assembly.GetTypes().OrderBy(type => type.FullName))
            {
                MemberInfo[] matches;
                try
                {
                    matches = requestedType.GetMembers(All | BindingFlags.DeclaredOnly)
                        .Where(member => requestedTerms.Any(term =>
                            member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                        .ToArray();
                }
                catch
                {
                    continue;
                }
                foreach (MemberInfo match in matches)
                {
                    Console.WriteLine((requestedType.FullName ?? requestedType.Name) + " :: " + Describe(match));
                }
            }
            return;
        }

        foreach (Type type in assembly.GetTypes()
                     .Where(type =>
                     {
                         string name = type.FullName ?? type.Name;
                         return name.Contains("nb", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("battle", StringComparison.OrdinalIgnoreCase);
                     })
                     .OrderBy(type => type.FullName))
        {
            MemberInfo[] members;
            try
            {
                bool dumpAll = type.Name == "nbCommSelProcessData_t" ||
                               type.Name == "nbActionProcessData_t" ||
                               type.Name == "nbTarSelProcessData_t" ||
                               type.Name == "nbTarSelProcess";
                members = type.GetMembers(All | BindingFlags.DeclaredOnly)
                    .Where(member => dumpAll || SpeedTerms.Any(term =>
                        member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    .Where(member => member is MethodInfo || member is PropertyInfo ||
                                     (member is FieldInfo field &&
                                      !field.Name.StartsWith("Native", StringComparison.Ordinal)))
                    .OrderBy(member => member.Name)
                    .ToArray();
            }
            catch
            {
                continue;
            }

            if (members.Length == 0)
            {
                continue;
            }

            Console.WriteLine("\n--- " + type.FullName + " ---");
            foreach (MemberInfo member in members)
            {
                Console.WriteLine(Describe(member));
            }
        }
    }

    private static string Describe(MemberInfo member)
    {
        if (member is MethodInfo method)
        {
            string parameters = string.Join(", ", method.GetParameters()
                .Select(parameter => parameter.ParameterType.FullName + " " + parameter.Name));
            return $"METHOD {method.ReturnType.FullName} {method.Name}({parameters})";
        }
        if (member is FieldInfo field)
        {
            return $"FIELD {field.FieldType.FullName} {field.Name}";
        }
        if (member is PropertyInfo property)
        {
            return $"PROPERTY {property.PropertyType.FullName} {property.Name}";
        }
        return member.MemberType + " " + member.Name;
    }
}
