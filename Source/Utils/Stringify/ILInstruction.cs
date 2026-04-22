using System.Reflection;
using System.Reflection.Emit;
using LanguageCore.IL.Reflection;

namespace LanguageCore;

public static partial class Stringifier
{
    public static void Stringify(InlineNoneInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode}");
    }
    public static void Stringify(InlineBrTargetInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} IL_{instruction.TargetOffset:x4}");
    }
    public static void Stringify(InlineLabelInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} ");
#if NETSTANDARD
        builder.Append($"L_?");
#else
        builder.Append($"L_{instruction.Label.Id}");
#endif
    }
    public static void Stringify(InlineLocalInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} {instruction.Local.LocalIndex}");
    }
    public static void Stringify(ShortInlineBrTargetInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} IL_{instruction.TargetOffset:x4}");
    }
    public static void Stringify(InlineSwitchInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} ");
        builder.Append('(');
        for (int i = 0; i < instruction.TargetOffsets.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
                builder.Append(' ');
            }

            builder.Append($"IL_{instruction.TargetOffsets[i]:x4}");
        }
        builder.Append(')');
    }
    public static void Stringify(InlineIInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} {instruction.Int32}");
    }
    public static void Stringify(InlineI8Instruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} {instruction.Int64}");
    }
    public static void Stringify(ShortInlineIInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} {instruction.Byte}");
    }
    public static void Stringify(InlineRInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} {instruction.Double}d");
    }
    public static void Stringify(ShortInlineRInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} {instruction.Single}f");
    }
    public static void Stringify(InlineFieldInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} ");
        try
        {
            builder.Append($"{instruction.Field.FieldType} {instruction.Field.DeclaringType}.{instruction.Field.Name}");
        }
        catch (Exception ex)
        {
            builder.Append($"!{ex.Message}!");
        }
    }
    public static void Stringify(InlineMethodInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} ");
        try
        {
            builder.Append(instruction.Method.DeclaringType is null ? instruction.Method.ToString()! : $"{instruction.Method}/{instruction.Method.DeclaringType}");
        }
        catch (Exception ex)
        {
            builder.Append($"!{ex.Message}!");
        }
    }
    public static void Stringify(InlineTypeInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} ");
        try
        {
            builder.Append(instruction.Type.ToString());
        }
        catch (Exception ex)
        {
            builder.Append($"!{ex.Message}!");
        }
    }
    public static void Stringify(InlineSigInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} ");
        builder.Append("SIG [");
        for (int i = 0; i < instruction.Signature.Length; i++)
        {
            if (i > 0) builder.Append(' ');
            builder.Append(instruction.Signature[i].ToString("X2"));
        }
        builder.Append(']');
    }
    public static void Stringify(InlineTokInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} ");
        try
        {
            builder.Append($"{instruction.Member}/{instruction.Member.DeclaringType}");
        }
        catch (Exception ex)
        {
            builder.Append($"!{ex.Message}!");
        }
    }
    public static void Stringify(InlineStringInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} ");
        builder.Append('"');
        for (int i = 0; i < instruction.String.Length; i++)
        {
            char ch = instruction.String[i];
            switch (ch)
            {
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case < (char)0x20 or >= (char)0x7f:
                    builder.Append($"\\u{ch:x4}");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }
        builder.Append('"');
    }
    public static void Stringify(InlineVarInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} ");
        if (instruction.OpCode == OpCodes.Ldarg || instruction.OpCode == OpCodes.Ldarga || instruction.OpCode == OpCodes.Starg)
        {
            if (method is not null)
            {
                ParameterInfo[] parameters = method.GetParameters();
                int i = instruction.Ordinal;

                if (!method.IsStatic)
                {
                    if (i == 0)
                    {
                        builder.Append("this");
                        return;
                    }
                    i--;
                }

                if (i >= 0 && i < parameters.Length)
                {
                    ParameterInfo p = parameters[i];
                    if (p.Name is not null)
                    {
                        builder.Append(p.Name);
                        return;
                    }
                }
            }
            builder.Append($"p{instruction.Ordinal}");
        }
        else if (instruction.OpCode == OpCodes.Ldloc || instruction.OpCode == OpCodes.Ldloca || instruction.OpCode == OpCodes.Stloc)
        {
            builder.Append($"l{instruction.Ordinal}");
        }
        else
        {
            builder.Append($"v{instruction.Ordinal}");
        }
    }
    public static void Stringify(ShortInlineVarInstruction instruction, Builder builder, MethodBase? method)
    {
        builder.Append($"{instruction.OpCode,-10} ");
        if (instruction.OpCode == OpCodes.Ldarga_S || instruction.OpCode == OpCodes.Ldarg_S || instruction.OpCode == OpCodes.Starg_S)
        {
            if (method is not null)
            {
                ParameterInfo[] parameters = method.GetParameters();
                int i = instruction.Ordinal;

                if (!method.IsStatic)
                {
                    if (i == 0)
                    {
                        builder.Append("this");
                        return;
                    }
                    i--;
                }

                if (i >= 0 && i < parameters.Length)
                {
                    ParameterInfo p = parameters[i];
                    if (p.Name is not null)
                    {
                        builder.Append(p.Name);
                        return;
                    }
                }
            }
            builder.Append($"p{instruction.Ordinal}");
        }
        else if (instruction.OpCode == OpCodes.Ldloca_S || instruction.OpCode == OpCodes.Stloc_S)
        {
            builder.Append($"l{instruction.Ordinal}");
        }
        else
        {
            builder.Append($"v{instruction.Ordinal}");
        }
    }

    public static void Stringify(ILInstruction instruction, Builder builder, MethodBase? method)
    {
        switch (instruction)
        {
            case InlineNoneInstruction v: Stringify(v, builder, method); break;
            case InlineBrTargetInstruction v: Stringify(v, builder, method); break;
            case InlineLabelInstruction v: Stringify(v, builder, method); break;
            case InlineLocalInstruction v: Stringify(v, builder, method); break;
            case ShortInlineBrTargetInstruction v: Stringify(v, builder, method); break;
            case InlineSwitchInstruction v: Stringify(v, builder, method); break;
            case InlineIInstruction v: Stringify(v, builder, method); break;
            case InlineI8Instruction v: Stringify(v, builder, method); break;
            case ShortInlineIInstruction v: Stringify(v, builder, method); break;
            case InlineRInstruction v: Stringify(v, builder, method); break;
            case ShortInlineRInstruction v: Stringify(v, builder, method); break;
            case InlineFieldInstruction v: Stringify(v, builder, method); break;
            case InlineMethodInstruction v: Stringify(v, builder, method); break;
            case InlineTypeInstruction v: Stringify(v, builder, method); break;
            case InlineSigInstruction v: Stringify(v, builder, method); break;
            case InlineTokInstruction v: Stringify(v, builder, method); break;
            case InlineStringInstruction v: Stringify(v, builder, method); break;
            case InlineVarInstruction v: Stringify(v, builder, method); break;
            case ShortInlineVarInstruction v: Stringify(v, builder, method); break;
            default: throw new UnreachableException(instruction.GetType().Name);
        }
    }
}
