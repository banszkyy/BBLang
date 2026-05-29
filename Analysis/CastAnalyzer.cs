using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LanguageCore.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CastAnalyzer : DiagnosticAnalyzer
    {
        static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            id: "MY003",
            title: "Don't cast random types",
            messageFormat: "Don't cast '{0}' to '{1}'",
            category: "Quality",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeSyntaxNode, SyntaxKind.CastExpression);
        }

        static readonly ImmutableHashSet<string> SkipTypes = ImmutableHashSet.Create("byte", "sbyte", "short", "ushort", "char", "int", "uint", "long", "ulong", "float");

        void AnalyzeSyntaxNode(SyntaxNodeAnalysisContext context)
        {
            if (!(context.Node is CastExpressionSyntax cast)) return;

            ITypeSymbol sourceType = context.SemanticModel.GetTypeInfo(cast.Expression).ConvertedType;
            ITypeSymbol targetType = context.SemanticModel.GetTypeInfo(cast.Type).Type;
            if (sourceType == null) return;
            if (targetType == null) return;

            if (targetType.TypeKind == TypeKind.Enum || targetType.TypeKind == TypeKind.Struct) return;
            if (SkipTypes.Contains(targetType.ToString())) return;

            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), sourceType, targetType));
        }
    }
}
