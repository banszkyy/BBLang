using System.Reflection;
using System.Reflection.Emit;
using LanguageCore.Compiler;
using LanguageCore.Runtime;

namespace LanguageCore.IL.Generator;

public partial class CodeGeneratorForIL : CodeGenerator
{
    public static readonly unsafe CompilerSettings DefaultCompilerSettings = new()
    {
        RuntimeInfo = new()
        {
            PointerSize = sizeof(nint),
        },
        ArrayLengthType = BuiltinType.I32,
        BooleanType = BuiltinType.U8,
        ExitCodeType = BuiltinType.I32,
        SizeofStatementType = BuiltinType.I32,
        Optimizations = OptimizationSettings.All,
        PreprocessorVariables = PreprocessorVariables.IL,
        ExternalConstants = ImmutableArray<ExternalConstant>.Empty,
        ExternalFunctions = ImmutableArray<IExternalFunction>.Empty,
        SourceProviders = ImmutableArray.Create<ISourceProvider>(
            FileSourceProvider.Instance
        ),
    };

    protected override unsafe RuntimeInfo RuntimeInfo => new()
    {
        PointerSize = sizeof(nint),
    };

    readonly ILGeneratorSettings Settings;
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)]
    readonly Type GlobalContextType;
    readonly FieldInfo GlobalContextType_Targets;
    readonly List<object> DelegateTargets = new();
    readonly Dictionary<CompiledVariableDefinition, FieldInfo> EmittedGlobalVariables = new();

    public CodeGeneratorForIL(CompilerResult compilerResult, DiagnosticsCollection diagnostics, ILGeneratorSettings settings, ModuleBuilder? module) : base(compilerResult, diagnostics)
    {
        Builders = new();

        if (module is null)
        {
            AssemblyBuilder assemBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName()
            {
                Name = "BBLangGeneratedAssembly",
            }, AssemblyBuilderAccess.RunAndCollect);

            Module = assemBuilder.DefineDynamicModule("BBLangGeneratedModule");
        }
        else
        {
            Module = module;
        }

        TypeBuilder globalContextType = Module.DefineType("__GlobalContext", TypeAttributes.Public | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, typeof(object));

        HashSet<string> definedFields = new();

        string targetsFieldName = Utils.MakeUnique("targets", v => !definedFields.Contains(v));

        globalContextType.DefineField(targetsFieldName, typeof(object[]), FieldAttributes.Public | FieldAttributes.Static);
        definedFields.Add(targetsFieldName);

        Dictionary<CompiledVariableDefinition, string> variableFieldMap = new();

        foreach (CompiledVariableDefinition globalVariable in compilerResult.Statements.OfType<CompiledVariableDefinition>())
        {
            if (!globalVariable.IsGlobal) continue;

            if (!ToType(globalVariable.Type, out Type? type, out PossibleDiagnostic? typeError))
            {
                Diagnostics.Add(typeError.ToError(globalVariable));
                continue;
            }
            string fieldName = Utils.MakeUnique($"g_{globalVariable.Identifier}", v => !definedFields.Contains(v));
            variableFieldMap[globalVariable] = fieldName;
            globalContextType.DefineField(fieldName, type, FieldAttributes.Public | FieldAttributes.Static);
            definedFields.Add(fieldName);
        }

        GlobalContextType = globalContextType.CreateType();

        GlobalContextType_Targets = GlobalContextType.GetField(targetsFieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) ?? throw new NullReferenceException();

        foreach (KeyValuePair<CompiledVariableDefinition, string> item in variableFieldMap)
        {
            EmittedGlobalVariables.Add(item.Key, GlobalContextType.GetField(item.Value, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) ?? throw new NullReferenceException());
        }

        Settings = settings;
    }
}
