using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Public-API tracking for the published CanKit.Pro L2 packages (1.0 readiness, Phase D):
/// renders each assembly's public surface (types + public/protected member signatures) and
/// compares it against the checked-in approved file under
/// <c>tests/CanKit.Tests/ApiApprovals/&lt;PackageId&gt;.approved.txt</c>. Any API change
/// fails the test and drops a <c>.received.txt</c> next to the approval so the reviewer can
/// see exactly what changed — updating the approval is a deliberate act in the same PR.
/// </summary>
public class PublicApiSurfaceTests
{
    private static readonly (string PackageId, string AssemblyName)[] Tracked =
    {
        ("CanKit.Pro.Actor", "CanKit.Pro.Actor"),
        ("CanKit.Pro.Addressing", "CanKit.Pro.Addressing"),
        ("CanKit.Pro.RawCan", "CanKit.Pro.RawCan"),
        ("CanKit.Pro.Reliability", "CanKit.Pro.Reliability"),
    };

    [Theory]
    [MemberData(nameof(TrackedPackages))]
    public void Public_Api_Surface_Matches_The_Approved_File(string packageId, string assemblyName)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));
        var actual = Render(assembly);

        var approvalsDir = FindApprovalsDir();
        var approvedPath = Path.Combine(approvalsDir, $"{packageId}.approved.txt");
        if (!File.Exists(approvedPath))
        {
            Directory.CreateDirectory(approvalsDir);
            File.WriteAllText(Path.Combine(approvalsDir, $"{packageId}.received.txt"), actual);
            File.Exists(approvedPath).Should().BeTrue(
                $"no approved API file for {packageId} at {approvedPath} — create it from the .received.txt this test just dropped");
            return;
        }

        var approved = File.ReadAllText(approvedPath);
        if (!string.Equals(Normalize(approved), Normalize(actual), StringComparison.Ordinal))
        {
            File.WriteAllText(Path.Combine(approvalsDir, $"{packageId}.received.txt"), actual);
            approved.Should().Be(actual,
                $"public API of {packageId} changed — review {packageId}.received.txt vs .approved.txt " +
                "and update the approval in the same PR if the change is intended");
        }
    }

    public static IEnumerable<object[]> TrackedPackages
        => Tracked.Select(t => new object[] { t.PackageId, t.AssemblyName });

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n").Trim();

    private static string FindApprovalsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CanKit.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (CanKit.sln).");
        }
        return Path.Combine(dir.FullName, "tests", "CanKit.Tests", "ApiApprovals");
    }

    // Canonical, diff-stable rendering of the assembly's public API.
    private static string Render(Assembly assembly)
    {
        var sb = new StringBuilder();
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            sb.AppendLine(FormatType(type));
            foreach (var member in FormatMembers(type))
            {
                sb.AppendLine("  " + member);
            }
        }
        return sb.ToString();
    }

    private static string FormatType(Type type)
    {
        var kind = type switch
        {
            _ when type.IsInterface => "interface",
            _ when type.IsEnum => "enum",
            _ when type.IsValueType => "struct",
            _ => "class",
        };
        var suffix = type.IsEnum
            ? " : " + Enum.GetUnderlyingType(type).Name
            : string.Empty;
        return $"{kind} {type.FullName}{suffix}";
    }

    private static IEnumerable<string> FormatMembers(Type type)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        var lines = new List<string>();

        foreach (var field in type.GetFields(Flags).Where(f => !f.IsSpecialName))
        {
            lines.Add($"field {FormatTypeName(field.FieldType)} {field.Name}");
        }
        foreach (var property in type.GetProperties(Flags))
        {
            var accessors = string.Join("/",
                new[] { property.CanRead ? "get" : null, property.CanWrite ? "set" : null }
                    .Where(a => a is not null));
            lines.Add($"prop {FormatTypeName(property.PropertyType)} {property.Name} {{{accessors}}}");
        }
        foreach (var evt in type.GetEvents(Flags))
        {
            lines.Add($"event {FormatTypeName(evt.EventHandlerType!)} {evt.Name}");
        }
        foreach (var method in type.GetMethods(Flags).Where(m => !m.IsSpecialName))
        {
            var generic = method.IsGenericMethodDefinition
                ? $"<{string.Join(",", method.GetGenericArguments().Select(a => a.Name))}>"
                : string.Empty;
            var pars = string.Join(", ", method.GetParameters()
                .Select(p => $"{FormatTypeName(p.ParameterType)} {p.Name}"));
            lines.Add($"method {FormatTypeName(method.ReturnType)} {method.Name}{generic}({pars})");
        }
        foreach (var ctor in type.GetConstructors(Flags))
        {
            var pars = string.Join(", ", ctor.GetParameters()
                .Select(p => $"{FormatTypeName(p.ParameterType)} {p.Name}"));
            lines.Add($"ctor ({pars})");
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static string FormatTypeName(Type type)
    {
        if (type.IsGenericParameter) return type.Name;
        if (type.IsArray) return FormatTypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            var name = def.FullName!;
            var tick = name.IndexOf('`');
            if (tick >= 0) name = name.Substring(0, tick);
            var args = string.Join(",", type.GetGenericArguments().Select(FormatTypeName));
            return $"{name}<{args}>";
        }
        return type.FullName ?? type.Name;
    }
}
