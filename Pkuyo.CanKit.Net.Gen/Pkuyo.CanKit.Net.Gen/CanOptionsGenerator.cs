using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;


namespace System.Runtime.CompilerServices
{
    internal class IsExternalInit { }
}

namespace Pkuyo.CanKit.Net.Gen
{
    public enum CanOptionType
    {
        Init = 1,
        Runtime = 2
    }
    [Generator(LanguageNames.CSharp)]
    public sealed class CanOptionsGenerator : IIncrementalGenerator
    {
        private const string OptionAttrFull = "Pkuyo.CanKit.Net.Core.Attributes.CanOptionAttribute";
        private const string OptionAttrShort = "CanOptionAttribute";
        private const string ParamAttrFull = "Pkuyo.CanKit.Net.Core.Attributes.CanOptionItemAttribute";
        private const string ParamAttrShort = "CanOptionItemAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // partial + [CanOptions]
            var candidates = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) =>
                    node is TypeDeclarationSyntax t &&
                    t.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)) &&
                    t.AttributeLists.Count > 0,
                static (ctx, ct) => Transform(ctx, ct)
            ).Where(static t => t is not null)!;

            var combo = context.CompilationProvider.Combine(candidates.Collect());

            context.RegisterSourceOutput(combo, static (spc, tuple) =>
            {
                var (compilation, items) = (tuple.Left, tuple.Right);
                if (items.Length == 0) return;

                foreach (var item in items!)
                {
                    Emit(spc, compilation, item!);
                }
            });
        }

        private static TypeWork? Transform(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
        {
            var typeDecl = (TypeDeclarationSyntax)ctx.Node;
            var symbol = ctx.SemanticModel.GetDeclaredSymbol(typeDecl, ct);
            if (symbol is null) return null;

            if (!HasAttr(symbol, OptionAttrFull, OptionAttrShort)) return null;

            // collect partial + [CanOptionItem]
            var list = new List<PropertyWork>();
            foreach (var member in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
            {
                if (!member.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                    continue;

                // no method body
                if (member.AccessorList is null) continue;
                var allAccessorsAreAuto =
                    member.AccessorList.Accessors.All(a => a.Body is null && a.ExpressionBody is null);
                if (!allAccessorsAreAuto) continue;

                var propSymbol = ctx.SemanticModel.GetDeclaredSymbol(member, ct);
                if (propSymbol is null || propSymbol.IsStatic) continue;

                var attr = GetAttr(propSymbol, ParamAttrFull, ParamAttrShort);
                if (attr == null || attr.ConstructorArguments.Any(i => i.Kind == TypedConstantKind.Error))
                    continue;

                var hasGet = propSymbol.GetMethod is not null;
                var hasSet = propSymbol.SetMethod is not null &&
                             propSymbol.SetMethod.DeclaredAccessibility != Accessibility.NotApplicable;
                var isInit = member.AccessorList.Accessors.Any(a =>
                    a.Kind() == SyntaxKind.InitAccessorDeclaration);

                list.Add(new PropertyWork(
                    Name: propSymbol.Name,
                    TypeDisplay: propSymbol.Type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)),
                    HasGet: hasGet,
                    HasSet: hasSet,
                    Accessibility: propSymbol.DeclaredAccessibility,
                    HasModifers: member.Modifiers.Any(m =>
                            m.IsKind(SyntaxKind.PublicKeyword) ||
                            m.IsKind(SyntaxKind.PrivateKeyword) ||
                            m.IsKind(SyntaxKind.ProtectedKeyword) ||
                            m.IsKind(SyntaxKind.InternalKeyword)),
                    UseInit: isInit,
                    OptionName: (string)attr.ConstructorArguments[0].Value!,
                    OptionType: (CanOptionType)((int)attr.ConstructorArguments[1].Value!),
                    DefaultValue: attr.ConstructorArguments[2].Value as string
                ));
            }

            //if (list.Count == 0) return null;

            return new TypeWork(symbol, list);
        }

        private static AttributeData? GetAttr(ISymbol symbol, string full, string shortName)
            => symbol.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == full
                || a.AttributeClass?.Name == shortName);
        private static bool HasAttr(ISymbol symbol, string full, string shortName)
            => GetAttr(symbol, full, shortName) != null;
        private static void Emit(SourceProductionContext spc, Compilation compilation, TypeWork work)
        {
            var ns = work.TypeSymbol.ContainingNamespace?.IsGlobalNamespace == true
                ? null
                : work.TypeSymbol.ContainingNamespace?.ToDisplayString();

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.CodeDom.Compiler;");
            sb.AppendLine("using System.Collections;");

            if (ns is not null)
            {
                sb.Append("namespace ").Append(ns).AppendLine(";");
                sb.AppendLine();
            }


            var stack = new Stack<INamedTypeSymbol>();
            var cur = work.TypeSymbol;
            while (cur is not null)
            {
                stack.Push(cur);
                cur = cur.ContainingType;
            }

            INamedTypeSymbol? opened = null;
            foreach (var t in stack)
            {
                var kw = t.TypeKind switch
                {
                    TypeKind.Struct => "struct",
                    TypeKind.Class => "class",
                    TypeKind.Interface => "interface",
                    _ => "class"
                };
                sb.Append("partial ").Append(kw).Append(' ').Append(t.Name);

                if (t.Arity > 0)
                {
                    sb.Append('<');
                    for (int i = 0; i < t.TypeArguments.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(t.TypeArguments[i].Name);
                    }

                    sb.Append('>');
                }

                sb.AppendLine();
                sb.AppendLine("{");
                opened = t;
            }


            // backing field + property impl

            var avaiableProerties = new List<PropertyWork>();
            var avaiableIndex = 0;
            for (int i = 0; i < work.Properties.Count; i++)
            {
                var p = work.Properties[i];

                if (!p.HasGet || !p.HasSet)
                {
                    var descriptor = new DiagnosticDescriptor(
                        id: "CANG001",
                        title: "属性缺少partial get/set",
                        messageFormat: "{0}.{1}缺少{2}",
                        category: "SourceGenerator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true
                    );
                    var str = !p.HasGet ? "Get" : "";
                    if (!p.HasSet)
                        str += !string.IsNullOrEmpty(str) ? "/Set" : "Set";

                    var diagnostic = Diagnostic.Create(descriptor, Location.None,
                        work.TypeSymbol.Name,
                        p.Name,
                        str);
                    spc.ReportDiagnostic(diagnostic);
                    continue;
                }

                if (p.Accessibility == Accessibility.NotApplicable && p.HasModifers)
                {
                    var descriptor = new DiagnosticDescriptor(
                        id: "CANG002",
                        title: "属性访问权限错误",
                        messageFormat: "{0}.{1}访问权限描述符错误",
                        category: "SourceGenerator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true
                    );

                    var diagnostic = Diagnostic.Create(descriptor, Location.None,
                        work.TypeSymbol.Name,
                        p.Name);
                    spc.ReportDiagnostic(diagnostic);
                    continue;
                }

                avaiableProerties.Add(p);

                var backing = MakeBackingName(p.Name);

                // backing field
                sb.Append("    private ");
                sb.Append(p.TypeDisplay).Append(' ').Append(backing);

                if (p.DefaultValue != null)
                    sb.Append($" = {p.DefaultValue}");

                sb.AppendLine(";");
                // property impl
                sb.Append("    ");
                if (p.HasModifers)
                {
                    switch (p.Accessibility)
                    {
                        case Accessibility.Public:
                            sb.Append("public");
                            break;
                        case Accessibility.Private:
                            sb.Append("private");
                            break;
                        case Accessibility.Protected:
                            sb.Append("protected");
                            break;
                        case Accessibility.Internal:
                            sb.Append("internal");
                            break;
                        case Accessibility.ProtectedOrInternal:
                            sb.Append("protected internal");
                            break;
                        case Accessibility.ProtectedAndInternal:
                            sb.Append("private protected");
                            break;
                    }

                    sb.Append(" ");
                }

                sb.Append("partial ").Append(p.TypeDisplay).Append(' ').Append(p.Name).AppendLine();
                sb.AppendLine("    {");

                sb.Append("        get => ");
                sb.Append("this.");
                sb.Append(backing).AppendLine(";");

                var accessor = p.UseInit ? "init" : "set";
                sb.Append("        ").AppendLine(accessor).AppendLine("        {");
                sb.AppendLine($"            if(this.{backing} != value)");
                sb.AppendLine("            {");
                sb.AppendLine($"                this.{backing} = value;");
                sb.AppendLine($"                this._hasChanged[{avaiableIndex}] = true;");
                sb.AppendLine("            }");
                sb.AppendLine("        }");


                sb.AppendLine("    }");
                sb.AppendLine();

                avaiableIndex++;
            }

            sb.AppendLine();
            sb.AppendLine(
                $"    BitArray _hasChanged = new BitArray({avaiableProerties.Count});");
            sb.AppendLine();
            sb.AppendLine("    public partial void Apply(Pkuyo.CanKit.Net.Core.Abstractions.ICanApplier applier, bool force)");
            sb.AppendLine("    {");
            sb.AppendLine("         if(applier is Pkuyo.CanKit.Net.Core.Abstractions.INamedCanApplier namedApplier)");
            sb.AppendLine("         {");
            for (int i = 0; i < avaiableProerties.Count; i++)
            {
                var p = avaiableProerties[i];
                var backing = MakeBackingName(p.Name);
                sb.Append($"            if((_hasChanged[{i}] || force)");
                sb.Append($" && namedApplier.ApplierStatus == Pkuyo.CanKit.Net.Core.Definitions.CanOptionType.{p.OptionType}");
                sb.AppendLine($" && namedApplier.ApplyOne<{p.TypeDisplay}>(\"{p.OptionName}\",{backing}))");
                sb.AppendLine($"                _hasChanged[{i}] = false;");
            }
            sb.AppendLine("         }");
            sb.AppendLine($"        applier.Apply(this);");
            sb.AppendLine("    }");
            // 关闭嵌套
            while (stack.Count > 0)
            {
                sb.AppendLine("}");
                stack.Pop();
            }

            var hintName = "CanOption." + work.TypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty)
                .Replace('<', '_').Replace('>', '_')
                .Replace('.', '_').Replace('+', '_') + ".g.cs";

            spc.AddSource(hintName, sb.ToString());
        }

        private static string MakeBackingName(string propName)
        {
            if (string.IsNullOrEmpty(propName)) return "__can_item_";
            var first = char.ToLowerInvariant(propName[0]);
            var rest = propName.Length > 1 ? propName.Substring(1) : "";
            return $"__can_item_{first}{rest}";
        }

        private sealed record PropertyWork(
            string Name,
            string TypeDisplay,
            bool HasGet,
            bool HasSet,
            Accessibility Accessibility,
            bool HasModifers,
            bool UseInit,
            string OptionName,
            CanOptionType OptionType,
            string? DefaultValue = null
        );

        private sealed record TypeWork(INamedTypeSymbol TypeSymbol, IReadOnlyList<PropertyWork> Properties);
    }
}
