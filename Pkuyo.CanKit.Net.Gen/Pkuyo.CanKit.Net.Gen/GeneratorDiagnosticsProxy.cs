using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pkuyo.CanKit.Net.Gen
{
    
    internal static class GeneratorDiagnosticsCatalog
    {
        // Match IDs and categories documented in AnalyzerReleases.Shipped.md
        public static readonly DiagnosticDescriptor CANG001 = new(
            id: "CANG001",
            title: "属性缺少 partial get/set",
            messageFormat: "{0}.{1}缺少{2}",
            category: "SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CANG002 = new(
            id: "CANG002",
            title: "属性访问权限错误",
            messageFormat: "{0}.{1}访问权限描述符错误",
            category: "SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class GeneratorDiagnosticsProxy : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                GeneratorDiagnosticsCatalog.CANG001,
                GeneratorDiagnosticsCatalog.CANG002
            );

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
        }
    }
}

