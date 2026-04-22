using System.Reflection;
using System.Reflection.Emit;
using LanguageCore.IL.Reflection;

namespace LanguageCore;

public static partial class Stringifier
{
    static void StringifyValue(object? value, Builder builder)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case string v:
                builder.Append('"');
                builder.Append(v.Escape());
                builder.Append('"');
                break;
            case Array array:
                Type type = array.GetType();
                Type elementType = type.GetElementType()!;
                int length = array.GetLength(0);
                builder.Append($"new {elementType}[{length}]");
                if (length > 0)
                {
                    builder.Append(" { ");
                    for (int i = 0; i < length; i++)
                    {
                        if (i > 0) builder.Append(", ");
                        StringifyValue(array.GetValue(i), builder);
                    }
                    builder.Append(" }");
                }
                break;
            default:
                builder.Append(value.ToString() ?? string.Empty);
                break;
        }
    }

    public static void Stringify(CustomAttributeData attribute, Builder builder)
    {
        builder.Append('[');
        string name = attribute.AttributeType.ToString();
        if (name.EndsWith("Attribute")) name = name[..^"Attribute".Length];
        builder.Append(name);
        builder.Append('(');
        bool w = false;
        foreach (CustomAttributeTypedArgument argument in attribute.ConstructorArguments)
        {
            if (w) builder.Append(", ");
            else w = true;
            StringifyValue(argument.Value, builder);
        }
        foreach (CustomAttributeNamedArgument argument in attribute.NamedArguments)
        {
            if (w) builder.Append(", ");
            else w = true;
            builder.Append(argument.MemberName);
            builder.Append(": ");
            StringifyValue(argument.TypedValue.Value, builder);
        }
        builder.Append(')');
        builder.Append(']');
    }

    public static void Stringify([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type, Builder builder)
    {
        foreach (CustomAttributeData item in type.GetCustomAttributesData())
        {
            Stringify(item, builder);
            builder.NewLine();
        }

        builder.Append($"{type.Attributes & TypeAttributes.VisibilityMask} ");

        foreach (TypeAttributes attribute in CompatibilityUtils.GetEnumValues<TypeAttributes>()
            .Where(v => (v & TypeAttributes.VisibilityMask) == 0 && v != 0))
        {
            if (type.Attributes.HasFlag(attribute))
            {
                builder.Append($"{attribute} ");
            }
        }

        builder.Append(type.Name);

        if (type.BaseType is not null)
        {
            builder.Space();
            builder.Append(':');
            builder.Space();
            builder.Append(type.BaseType.Name);
        }

        builder.NewLine();
        builder.Append('{');
        builder.IndentLevel++;

        MemberInfo[] members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        foreach (FieldInfo field in members.OfType<FieldInfo>())
        {
            builder.NewLine();
            Stringify(field, builder);
        }

        foreach (ConstructorInfo constructor in members.OfType<ConstructorInfo>())
        {
            builder.NewLine();
            builder.NewLine();
            Stringify(constructor, builder);
        }

        foreach (MethodInfo method in members.OfType<MethodInfo>())
        {
            builder.NewLine();
            builder.NewLine();
            Stringify(method, builder);
        }

        foreach (MemberInfo member in members)
        {
            switch (member)
            {
                case FieldInfo:
                case MethodInfo:
                case ConstructorInfo: break;
                default: throw new NotImplementedException(member.GetType().ToString());
            }
        }

        builder.IndentLevel--;
        builder.NewLine();
        builder.Append('}');
    }

    public static void Stringify(FieldInfo field, Builder builder)
    {
        foreach (CustomAttributeData item in field.GetCustomAttributesData())
        {
            Stringify(item, builder);
            builder.NewLine();
        }

        builder.Append($"{field.Attributes & FieldAttributes.FieldAccessMask} ");

        foreach (FieldAttributes attribute in CompatibilityUtils.GetEnumValues<FieldAttributes>()
            .Where(v => (v & FieldAttributes.FieldAccessMask) == 0 && v != 0))
        {
            if (field.Attributes.HasFlag(attribute))
            {
                builder.Append($"{attribute} ");
            }
        }

        builder.Append(field.FieldType.ToString());
        builder.Append(' ');
        builder.Append(field.Name);

        object? value = field.IsStatic ? field.GetValue(null) : null;
        if (value is not null)
        {
            builder.Append(" = ");
            StringifyValue(value, builder);
        }

        builder.Append(';');
    }

    static void StringifySignature(MethodInfo method, Builder builder)
    {
        try
        {
            foreach (CustomAttributeData item in method.GetCustomAttributesData())
            {
                Stringify(item, builder);
                builder.NewLine();
            }
        }
        catch
        {

        }

        builder.Append($"{method.Attributes & MethodAttributes.MemberAccessMask} ");

        foreach (MethodAttributes attribute in CompatibilityUtils.GetEnumValues<MethodAttributes>()
            .Where(v => (v & MethodAttributes.MemberAccessMask) == 0 && v != 0))
        {
            if (method.Attributes.HasFlag(attribute))
            {
                builder.Append($"{attribute} ");
            }
        }

        builder.Append(method.ReturnType.ToString());
        builder.Append(' ');
        builder.Append(method.Name);
        builder.Append('(');
        ParameterInfo[] parameters = method.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0) builder.Append(", ");
            ParameterInfo parameter = parameters[i];
            foreach (ParameterAttributes attribute in CompatibilityUtils.GetEnumValues<ParameterAttributes>().Where(v => v is not ParameterAttributes.None))
            {
                if (parameter.Attributes.HasFlag(attribute))
                {
                    builder.Append($"{attribute} ");
                }
            }
            builder.Append(parameter.ParameterType.ToString());
            builder.Append(' ');
            builder.Append(parameter.Name ?? $"p{i}");
        }
        builder.Append(')');
    }

    static void StringifySignature(ConstructorInfo method, Builder builder)
    {
        try
        {
            foreach (CustomAttributeData item in method.GetCustomAttributesData())
            {
                Stringify(item, builder);
                builder.NewLine();
            }
        }
        catch
        {

        }

        builder.Append($"{method.Attributes & MethodAttributes.MemberAccessMask} ");

        foreach (MethodAttributes attribute in CompatibilityUtils.GetEnumValues<MethodAttributes>()
            .Where(v => (v & MethodAttributes.MemberAccessMask) == 0 && v != 0))
        {
            if (method.Attributes.HasFlag(attribute))
            {
                builder.Append($"{attribute} ");
            }
        }

        builder.Append(method.Name);
        builder.Append('(');
        ParameterInfo[] parameters = method.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0) builder.Append(", ");
            ParameterInfo parameter = parameters[i];
            foreach (ParameterAttributes attribute in CompatibilityUtils.GetEnumValues<ParameterAttributes>().Where(v => v is not ParameterAttributes.None))
            {
                if (parameter.Attributes.HasFlag(attribute))
                {
                    builder.Append($"{attribute} ");
                }
            }
            builder.Append(parameter.ParameterType.ToString());
            builder.Append(' ');
            builder.Append(parameter.Name ?? $"p{i}");
        }
        builder.Append(')');
    }

    static void StringifyBody(MethodBody? body, ILReader? code, MethodBase method, Builder builder)
    {
        if (body is null && code is null)
        {
            builder.Append(';');
            return;
        }

        builder.NewLine();
        builder.Append('{');
        builder.IndentLevel++;

        if (body is not null)
        {
            builder.NewLine();
            builder.Append($".maxstack {body.MaxStackSize}");

            if (body.LocalVariables.Count > 0)
            {
                builder.NewLine();
                builder.Append(".locals ");
                if (body.InitLocals) builder.Append("init ");
                builder.Append('(');
                builder.IndentLevel++;

                foreach (LocalVariableInfo localVariable in body.LocalVariables)
                {
                    builder.NewLine();
                    builder.Append(localVariable.LocalType.ToString());
                    builder.Append(' ');
                    builder.Append($"l{localVariable.LocalIndex}");
                }

                builder.IndentLevel--;
                builder.NewLine();
                builder.Append(')');
            }
        }

        if (code is null)
        {
            builder.NewLine();
            builder.Append("// IL isn't avaliable");
        }
        else
        {
            foreach (ILInstruction instruction in code)
            {
                builder.NewLine();
                Stringify(UnshortenInstruction(instruction), builder, method);
            }
        }

        builder.IndentLevel--;
        builder.NewLine();
        builder.Append('}');
    }

    public static void Stringify(MethodInfo method, Builder builder)
    {
        StringifySignature(method, builder);

        MethodBody? body = null;

        try
        {
            body = method.GetMethodBody();
        }
        catch
        {
        }

        if (method is DynamicMethod dynamicMethod)
        {
            byte[]? codeBytes = DynamicMethodILProvider.GetByteArray(dynamicMethod);
            ILReader? code = codeBytes is null ? null : new ILReader(codeBytes, new DynamicScopeTokenResolver(dynamicMethod));

            StringifyBody(body, code, method, builder);
        }
        else
        {
            byte[]? codeBytes = body?.GetILAsByteArray();
            ILReader? code = codeBytes is null ? null : new ILReader(codeBytes, new ModuleScopeTokenResolver(method));

            StringifyBody(body, code, method, builder);
        }
    }

    public static void Stringify(ConstructorInfo constructor, Builder builder)
    {
        StringifySignature(constructor, builder);

        MethodBody? body = null;

        try
        {
            body = constructor.GetMethodBody();
        }
        catch
        {
        }

        if (constructor is ConstructorBuilder constructorBuilder)
        {
            byte[]? codeBytes = DynamicMethodILProvider.GetByteArray(constructorBuilder);
            ILReader? code = codeBytes is null ? null : new ILReader(codeBytes, new DynamicScopeTokenResolver(constructorBuilder));

            StringifyBody(body, code, constructor, builder);
        }
        else
        {
            byte[]? codeBytes = body?.GetILAsByteArray();
            ILReader? code = codeBytes is null ? null : new ILReader(codeBytes, new ModuleScopeTokenResolver(constructor));

            StringifyBody(body, code, constructor, builder);
        }
    }

    static ILInstruction UnshortenInstruction(ILInstruction instruction)
    {
        if (instruction.OpCode == OpCodes.Ldarg_0) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarg, 0);
        if (instruction.OpCode == OpCodes.Ldarg_1) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarg, 1);
        if (instruction.OpCode == OpCodes.Ldarg_2) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarg, 2);
        if (instruction.OpCode == OpCodes.Ldarg_3) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarg, 3);

        if (instruction.OpCode == OpCodes.Ldloc_0) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloc, 0);
        if (instruction.OpCode == OpCodes.Ldloc_1) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloc, 1);
        if (instruction.OpCode == OpCodes.Ldloc_2) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloc, 2);
        if (instruction.OpCode == OpCodes.Ldloc_3) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloc, 3);

        if (instruction.OpCode == OpCodes.Stloc_0) return new InlineVarInstruction(instruction.Offset, OpCodes.Stloc, 0);
        if (instruction.OpCode == OpCodes.Stloc_1) return new InlineVarInstruction(instruction.Offset, OpCodes.Stloc, 1);
        if (instruction.OpCode == OpCodes.Stloc_2) return new InlineVarInstruction(instruction.Offset, OpCodes.Stloc, 2);
        if (instruction.OpCode == OpCodes.Stloc_3) return new InlineVarInstruction(instruction.Offset, OpCodes.Stloc, 3);

        if (instruction.OpCode == OpCodes.Ldc_I4_0) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 0);
        if (instruction.OpCode == OpCodes.Ldc_I4_1) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 1);
        if (instruction.OpCode == OpCodes.Ldc_I4_2) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 2);
        if (instruction.OpCode == OpCodes.Ldc_I4_3) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 3);
        if (instruction.OpCode == OpCodes.Ldc_I4_4) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 4);
        if (instruction.OpCode == OpCodes.Ldc_I4_5) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 5);
        if (instruction.OpCode == OpCodes.Ldc_I4_6) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 6);
        if (instruction.OpCode == OpCodes.Ldc_I4_7) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 7);
        if (instruction.OpCode == OpCodes.Ldc_I4_8) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, 8);
        if (instruction.OpCode == OpCodes.Ldc_I4_M1) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, -1);

        if (instruction is ShortInlineVarInstruction s0)
        {
            if (instruction.OpCode == OpCodes.Ldarg_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarg, s0.Ordinal);
            if (instruction.OpCode == OpCodes.Ldloc_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloc, s0.Ordinal);
            if (instruction.OpCode == OpCodes.Stloc_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Stloc, s0.Ordinal);
            if (instruction.OpCode == OpCodes.Starg_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Starg, s0.Ordinal);
            if (instruction.OpCode == OpCodes.Ldarga_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldarga, s0.Ordinal);
            if (instruction.OpCode == OpCodes.Ldloca_S) return new InlineVarInstruction(instruction.Offset, OpCodes.Ldloca, s0.Ordinal);
        }

        if (instruction is ShortInlineIInstruction s1)
        {
            if (instruction.OpCode == OpCodes.Ldc_I4_S) return new InlineIInstruction(instruction.Offset, OpCodes.Ldc_I4, s1.Byte);
        }

        return instruction;
    }
}
