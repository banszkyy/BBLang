using LanguageCore.Compiler;
using LanguageCore.IR;
using LanguageCore.Runtime;

namespace LanguageCore.BBLang.Generator;

public partial class CodeGeneratorForMain : CodeGenerator
{
    readonly Dictionary<IRBlock, InstructionLabel> GeneratedBlocks = new();
    readonly Queue<IRBlock> RemainingBlocks = new();
    readonly Dictionary<IRTemporary, int> Temporaries = new();

    void EmitIR(IRTemporary value)
    {
        if (!Temporaries.TryGetValue(value, out int offset))
        {
            throw new UnreachableException();
        }
        PushFrom(new AddressOffset(Register.BasePointer, -offset), FindSize(value.Type));
    }
    void EmitIR(IRConstant value)
    {
        Push(value.Value);
    }
    void EmitIR(IRPhi value)
    {
        throw new NotImplementedException();
    }
    void EmitIR(IRCompilerVariable value)
    {
        Code.Emit(Opcode.Push, new PreparationInstructionOperand(new VariableInstructionOperand(value.Identifier)));
    }
    void EmitIR(IRValue value)
    {
        switch (value)
        {
            case IRTemporary v: EmitIR(v); break;
            case IRConstant v: EmitIR(v); break;
            case IRPhi v: EmitIR(v); break;
            case IRCompilerVariable v: EmitIR(v); break;
            default: throw new UnreachableException(value.GetType().Name);
        }
    }
    void EmitIR(IROperator statement)
    {
        using RegisterUsage.Auto leftReg = Registers.GetFree(FindBitWidth(statement.Left.Type, null!));
        using RegisterUsage.Auto rightReg = Registers.GetFree(FindBitWidth(statement.Right.Type, null!));
        EmitIR(statement.Left);
        PopTo(leftReg.Register);
        EmitIR(statement.Right);
        PopTo(rightReg.Register);

        switch (statement.Operator)
        {
            case IROperatorOp.Add:
                Code.Emit(Opcode.MathAdd, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.Subtract:
                Code.Emit(Opcode.MathSub, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.Divide:
                Code.Emit(Opcode.MathDivS, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.Multiply:
                Code.Emit(Opcode.MathMultS, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.Modulo:
                Code.Emit(Opcode.MathModS, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.BitshiftLeft:
                Code.Emit(Opcode.BitsShiftLeft, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.BitshiftRight:
                Code.Emit(Opcode.BitsShiftRight, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.BitwiseAnd:
                Code.Emit(Opcode.BitsAND, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.BitwiseOr:
                Code.Emit(Opcode.BitsOR, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.BitwiseXor:
                Code.Emit(Opcode.BitsXOR, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.LogicAnd:
                Code.Emit(Opcode.LogicAND, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.LogicOr:
                Code.Emit(Opcode.LogicOR, leftReg.Register, rightReg.Register);
                Push(leftReg.Register);
                break;
            case IROperatorOp.CompareGT:
            {
                Code.Emit(Opcode.Compare, leftReg.Register, rightReg.Register);

                InstructionLabel labelTrue = Code.DefineLabel();
                InstructionLabel labelSkipTrue = Code.DefineLabel();

                Code.Emit(Opcode.JumpIfGreaterS, labelTrue.Relative());
                Push(false);
                Code.Emit(Opcode.Jump, labelSkipTrue.Relative());
                Code.MarkLabel(labelTrue);
                Push(true);
                Code.MarkLabel(labelSkipTrue);

                Push(leftReg.Register);
                break;
            }
            case IROperatorOp.CompareGEQ:
            {
                Code.Emit(Opcode.Compare, leftReg.Register, rightReg.Register);

                InstructionLabel labelTrue = Code.DefineLabel();
                InstructionLabel labelSkipTrue = Code.DefineLabel();

                Code.Emit(Opcode.JumpIfGreaterOrEqualS, labelTrue.Relative());
                Push(false);
                Code.Emit(Opcode.Jump, labelSkipTrue.Relative());
                Code.MarkLabel(labelTrue);
                Push(true);
                Code.MarkLabel(labelSkipTrue);

                Push(leftReg.Register);
                break;
            }
            case IROperatorOp.CompareLT:
            {
                Code.Emit(Opcode.Compare, leftReg.Register, rightReg.Register);

                InstructionLabel labelTrue = Code.DefineLabel();
                InstructionLabel labelSkipTrue = Code.DefineLabel();

                Code.Emit(Opcode.JumpIfLessS, labelTrue.Relative());
                Push(false);
                Code.Emit(Opcode.Jump, labelSkipTrue.Relative());
                Code.MarkLabel(labelTrue);
                Push(true);
                Code.MarkLabel(labelSkipTrue);

                Push(leftReg.Register);
                break;
            }
            case IROperatorOp.CompareLEQ:
            {
                Code.Emit(Opcode.Compare, leftReg.Register, rightReg.Register);

                InstructionLabel labelTrue = Code.DefineLabel();
                InstructionLabel labelSkipTrue = Code.DefineLabel();

                Code.Emit(Opcode.JumpIfLessOrEqualS, labelTrue.Relative());
                Push(false);
                Code.Emit(Opcode.Jump, labelSkipTrue.Relative());
                Code.MarkLabel(labelTrue);
                Push(true);
                Code.MarkLabel(labelSkipTrue);

                Push(leftReg.Register);
                break;
            }
            case IROperatorOp.CompareEQ:
            {
                Code.Emit(Opcode.Compare, leftReg.Register, rightReg.Register);

                InstructionLabel labelTrue = Code.DefineLabel();
                InstructionLabel labelSkipTrue = Code.DefineLabel();

                Code.Emit(Opcode.JumpIfEqual, labelTrue.Relative());
                Push(false);
                Code.Emit(Opcode.Jump, labelSkipTrue.Relative());
                Code.MarkLabel(labelTrue);
                Push(true);
                Code.MarkLabel(labelSkipTrue);

                Push(leftReg.Register);
                break;
            }
            case IROperatorOp.CompareNEQ:
            {
                Code.Emit(Opcode.Compare, leftReg.Register, rightReg.Register);

                InstructionLabel labelTrue = Code.DefineLabel();
                InstructionLabel labelSkipTrue = Code.DefineLabel();

                Code.Emit(Opcode.JumpIfNotEqual, labelTrue.Relative());
                Push(false);
                Code.Emit(Opcode.Jump, labelSkipTrue.Relative());
                Code.MarkLabel(labelTrue);
                Push(true);
                Code.MarkLabel(labelSkipTrue);

                Push(leftReg.Register);
                break;
            }
            default: throw new UnreachableException(statement.Operator.ToString());
        }

        if (!Temporaries.TryGetValue(statement.Target, out int target))
        {
            target = Temporaries.Keys.Sum(v => FindSize(v.Type)) + BasePointerSize;
            Temporaries.Add(statement.Target, target);
        }

        if (ScopeSizes.Last != target)
        {
            PopTo(new AddressOffset(Register.BasePointer, -target), FindSize(statement.Target.Type));
        }
    }
    void EmitIR(IRAssignment statement)
    {
        if (!Temporaries.TryGetValue(statement.Target, out int target))
        {
            target = Temporaries.Keys.Sum(v => FindSize(v.Type)) + BasePointerSize;
            Temporaries.Add(statement.Target, target);
        }

        if (ScopeSizes.Last == target)
        {
            EmitIR(statement.Value);
        }
        else
        {
            EmitIR(statement.Value);
            PopTo(new AddressOffset(Register.BasePointer, -target), FindSize(statement.Value.Type));
        }
    }
    void EmitIR(IRReturn statement)
    {
        //EmitIR(statement.Value);
        Return();
    }
    void EmitIR(IRIndirectAssignment statement)
    {
        EmitIR(statement.Value);
        EmitIR(statement.TargetAddress);
        using (RegisterUsage.Auto reg = Registers.GetFree(Settings.PointerBitWidth))
        {
            PopTo(reg.Register);
            PopTo(new AddressRegisterPointer(reg.Register), FindSize(statement.Value.Type));
        }
    }
    void EmitIR(IRFunctionCall statement)
    {
        ILocated loc = new Location();

        CompiledFunction? f = Functions.FirstOrDefault(v => Utils.ReferenceEquals(v.Function, statement.Function.Template) && StatementCompiler.TypeArgumentsEquals(v.TypeArguments, statement.Function.TypeArguments));
        if (f is null)
        {
            Diagnostics.Add(DiagnosticAt.Internal($"Function \"{statement.Function.Template.ToReadable()}\" wasn't compiled", loc));
            return;
        }

        if (statement.Function.Template.ReturnSomething)
        {
            StackAlloc(FindSize(GeneralType.TryInsertTypeParameters(statement.Function.Template.Type, statement.Function.TypeArguments), loc), false);
        }

        foreach (IRValue item in statement.Arguments)
        {
            EmitIR(item);
        }

        InstructionLabel label = LabelForDefinition(statement.Function);
        Call(label, f.Flags.HasFlag(FunctionFlags.CapturesGlobalVariables), false);

        if (!label.IsMarked)
        { UndefinedFunctionOffsets.Add(new(loc, statement.Function.UnsafeTo<IHaveInstructionOffset>())); }

        if (statement.ReturnValue is not null)
        {
            if (!Temporaries.TryGetValue(statement.ReturnValue, out int target))
            {
                target = Temporaries.Keys.Sum(v => FindSize(v.Type)) + BasePointerSize;
                Temporaries.Add(statement.ReturnValue, target);
            }

            if (ScopeSizes.Last != target)
            {
                PopTo(new AddressOffset(Register.BasePointer, -target), FindSize(statement.ReturnValue.Type));
            }
        }
        else if (statement.Function.Template.ReturnSomething)
        {
            Pop(FindSize(GeneralType.TryInsertTypeParameters(statement.Function.Template.Type, statement.Function.TypeArguments), loc));
        }
    }
    void EmitIR(IRExternalFunctionCall statement)
    {
        ILocated loc = new Location();

        foreach (IRValue item in statement.Arguments)
        {
            EmitIR(item);
        }

        Code.Emit(Opcode.CallExternal, InstructionOperand.Immediate(statement.Function.Id));

        if (statement.ReturnValue is not null)
        {
            if (!Temporaries.TryGetValue(statement.ReturnValue, out int target))
            {
                target = Temporaries.Keys.Sum(v => FindSize(v.Type)) + BasePointerSize;
                Temporaries.Add(statement.ReturnValue, target);
            }

            if (ScopeSizes.Last != target)
            {
                PopTo(new AddressOffset(Register.BasePointer, -target), FindSize(statement.ReturnValue.Type));
            }
        }
        else if (statement.Function.ReturnValueSize > 0)
        {
            Pop(statement.Function.ReturnValueSize);
        }
    }
    void EmitIR(IRStatement statement)
    {
        switch (statement)
        {
            case IROperator v: EmitIR(v); break;
            case IRAssignment v: EmitIR(v); break;
            case IRReturn v: EmitIR(v); break;
            case IRIndirectAssignment v: EmitIR(v); break;
            case IRFunctionCall v: EmitIR(v); break;
            case IRExternalFunctionCall v: EmitIR(v); break;
            default: throw new UnreachableException(statement.GetType().Name);
        }
    }

    InstructionLabel GetBlockLabel(IRBlock block)
    {
        if (!GeneratedBlocks.TryGetValue(block, out InstructionLabel? v))
        {
            v = Code.DefineLabel();
            GeneratedBlocks[block] = v;
            RemainingBlocks.Enqueue(block);
        }
        return v;
    }

    void EmitIR(IRSimpleBlock block)
    {
        foreach (IRStatement item in block.Statements)
        {
            EmitIR(item);
        }

        if (block.Next is not null)
        {
            InstructionLabel label = GetBlockLabel(block.Next);
            Code.Emit(Opcode.Jump, new PreparationInstructionOperand(label, false));
        }
    }
    void EmitIR(IRBranch block)
    {
        foreach (IRStatement item in block.Statements)
        {
            EmitIR(item);
        }

        EmitIR(block.Condition);
        if (!FindBitWidth(block.Condition.Type, out BitWidth bw, out _)) throw new UnreachableException();
        using (RegisterUsage.Auto reg = Registers.GetFree(bw))
        {
            PopTo(reg.Register);
            Code.Emit(Opcode.Compare, reg.Register, new PreparationInstructionOperand(0));
            Code.Emit(Opcode.JumpIfNotEqual, new PreparationInstructionOperand(GetBlockLabel(block.True), false));
            Code.Emit(Opcode.Jump, new PreparationInstructionOperand(GetBlockLabel(block.False), false));
        }
    }
    void EmitIR(IRBlock block)
    {
        switch (block)
        {
            case IRSimpleBlock v: EmitIR(v); break;
            case IRBranch v: EmitIR(v); break;
            default: throw new UnreachableException(block.GetType().Name);
        }
    }

    void EmitIRAll(IRBlock block)
    {
        RemainingBlocks.Enqueue(block);
        while (RemainingBlocks.TryDequeue(out IRBlock? b))
        {
            InstructionLabel label = GeneratedBlocks.TryGetValue(b, out InstructionLabel? v) ? v : GeneratedBlocks[b] = Code.DefineLabel();
            Code.MarkLabel(label);
            EmitIR(b);
        }
    }
}
