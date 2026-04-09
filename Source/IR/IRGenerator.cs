using LanguageCore.Compiler;

namespace LanguageCore.IR;

abstract class IRValue
{
    public readonly GeneralType Type;

    protected IRValue(GeneralType type) => Type = type;

    public abstract override string ToString();
}

class IRTemporary : IRValue
{
    public readonly int Id;

    public IRTemporary(int id, GeneralType type) : base(type)
    {
        Id = id;
    }

    public override string ToString() => $"t{Id}";
}

class IRConstant : IRValue
{
    public required CompiledValue Value;

    public IRConstant(GeneralType type) : base(type)
    {
    }

    public override string ToString() => Value.ToString();
}

class IRPhi : IRValue
{
    public List<IRValue> Operands;
    public ImmutableArray<IRValue> Users;
    public IRBlock Block;

    public IRPhi(IRBlock block) : base(BuiltinType.Any)
    {
        Operands = new();
        Users = ImmutableArray<IRValue>.Empty;
        Block = block;
    }

    public void ReplaceBy(IRPhi same)
    {
        throw new NotImplementedException();
    }

    public override string ToString() => $"phi()";
}

abstract class IRStatement
{
    public abstract override string ToString();
}

enum IROperatorOp
{
    Add,
    Subtract,
    Divide,
    Multiply,
    Modulo,
    BitshiftLeft,
    BitshiftRight,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    LogicAnd,
    LogicOr,
    CompareGT,
    CompareGEQ,
    CompareLT,
    CompareLEQ,
    CompareEQ,
    CompareNEQ,
}

class IROperator : IRStatement
{
    public required IRTemporary Target;
    public required IROperatorOp Operator;
    public required IRValue Left;
    public required IRValue Right;

    public override string ToString() => $"{Target} = {Left} {Operator switch
    {
        IROperatorOp.Add => "+",
        IROperatorOp.Subtract => "-",
        IROperatorOp.Divide => "/",
        IROperatorOp.Multiply => "*",
        IROperatorOp.Modulo => "%",
        IROperatorOp.BitshiftLeft => "<<",
        IROperatorOp.BitshiftRight => ">>",
        IROperatorOp.BitwiseAnd => "&",
        IROperatorOp.BitwiseOr => "|",
        IROperatorOp.BitwiseXor => "^",
        IROperatorOp.LogicAnd => "&&",
        IROperatorOp.LogicOr => "||",
        IROperatorOp.CompareGT => ">",
        IROperatorOp.CompareGEQ => ">=",
        IROperatorOp.CompareLT => "<",
        IROperatorOp.CompareLEQ => "</",
        IROperatorOp.CompareEQ => "==",
        IROperatorOp.CompareNEQ => "!=",
        _ => "?",
    }} {Right}";
}

class IRAssignment : IRStatement
{
    public required IRTemporary Target;
    public required IRValue Value;

    public override string ToString() => $"{Target} = {Value}";
}

class IRReturn : IRStatement
{
    public IRValue? Value;

    public override string ToString() => Value is null ? $"return" : $"return {Value}";
}

abstract class IRBlock
{
    public int Id;
    public ImmutableArray<IRStatement> Statements;
    public readonly List<IRBlock> Preds;

    protected IRBlock(int id, ImmutableArray<IRStatement> statements)
    {
        Id = id;
        Preds = new();
        Statements = statements;
    }
}

class IRSimpleBlock : IRBlock
{
    public IRBlock? Next;

    public IRSimpleBlock(int id, ImmutableArray<IRStatement> statements) : base(id, statements)
    {
    }
}

class IRBranch : IRBlock
{
    public required IRValue Condition;
    public required IRBlock True;
    public required IRBlock False;

    public IRBranch(int id, ImmutableArray<IRStatement> statements) : base(id, statements)
    {
    }
}

class IRLocalVersion
{
    public required IRTemporary Temporary;
    public IRBlock? Block;
}

class IRLocal
{
    public readonly List<IRLocalVersion> Values = new();
}

class IRBuilder
{
    public IRBlock? PreviousBlock;
    public readonly List<IRStatement> Statements;
    public readonly List<IRTemporary> Temporaries;
    public readonly Dictionary<string, IRLocal> Locals;

    public readonly List<IRSimpleBlock> UnfinishedBlocks;
    public readonly List<IRLocalVersion> UnfinishedLocals;

    int BlockId;

    public IRBuilder()
    {
        PreviousBlock = null;
        Statements = new();
        Temporaries = new();
        Locals = new();

        UnfinishedBlocks = new();
        UnfinishedLocals = new();
    }

    public int NextBlockId() => ++BlockId;

    public void FinishLocals(IRBlock block)
    {
        foreach (IRAssignment statement in block.Statements.OfType<IRAssignment>())
        {
            for (int i = 0; i < UnfinishedLocals.Count; i++)
            {
                if (!Utils.ReferenceEquals(statement.Target, UnfinishedLocals[i].Temporary)) continue;
                UnfinishedLocals[i].Block = block;
                UnfinishedLocals.RemoveAt(i--);
            }
        }
    }

    public void FinishBlocks(IRBlock next)
    {
        foreach (IRSimpleBlock item in UnfinishedBlocks)
        {
            item.Next = next;
        }
        UnfinishedBlocks.Clear();
    }

    public ImmutableArray<IRStatement> CompileStatements()
    {
        ImmutableArray<IRStatement> result = Statements.ToImmutableArray();
        Statements.Clear();
        return result;
    }

    public IRTemporary NewTemporary(GeneralType type)
    {
        int id = 1;
        while (Temporaries.Any(v => v.Id == id))
        {
            id++;
        }
        IRTemporary result = new(id, type);
        Temporaries.Add(result);
        return result;
    }
}

class IRGenerator
{
    readonly CompilerResult CompilerResult;
    readonly Dictionary<string, Dictionary<IRBlock, IRValue>> Variables = new();
    readonly HashSet<IRBlock> SealedBlocks = new();

    void WriteVariable(string variable, IRBlock block, IRValue value)
    {
        (Variables[variable] ??= new())[block] = value;
    }

    IRValue ReadVariable(string variable, IRBlock block)
    {
        if (Variables[variable].TryGetValue(block, out IRValue? local))
        {
            return local;
        }
        else
        {
            return ReadVariableRecursive(variable, block);
        }
    }

    IRValue ReadVariableRecursive(string variable, IRBlock block)
    {
        IRValue val;
        if (!SealedBlocks.Contains(block))
        {
            val = new IRPhi(block);
        }
        else if (block.Preds.Count == 1)
        {
            val = ReadVariable(variable, block.Preds[0]);
        }
        else
        {
            IRPhi p = new(block);
            WriteVariable(variable, block, p);
            val = AddPhiOperands(variable, p);
        }
        WriteVariable(variable, block, val);
        return val;
    }

    IRValue AddPhiOperands(string variable, IRPhi phi)
    {
        foreach (IRBlock pred in phi.Block.Preds)
        {
            phi.Operands.Add(ReadVariable(variable, pred));
        }
        return TryRemoveTrivialPhi(phi);
    }

    IRValue TryRemoveTrivialPhi(IRPhi phi)
    {
        IRPhi? same = null;

        foreach (IRValue op in phi.Operands)
        {
            if (Utils.ReferenceEquals(op, same) || Utils.ReferenceEquals(op, phi))
            {
                continue;
            }

            if (same is not null)
            {
                return phi;
            }
        }

        if (same is null)
        {
            throw new NotImplementedException("The phi is unreachable or in the start block");
        }

        ImmutableArray<IRValue> users = phi.Users.Remove(phi);
        phi.ReplaceBy(same);

        foreach (IRValue use in users)
        {
            if (use is IRPhi usePhi)
            {
                TryRemoveTrivialPhi(usePhi);
            }
        }

        return same;
    }

    public IRGenerator(CompilerResult compilerResult)
    {
        CompilerResult = compilerResult;
    }

    IRValue EmitExpression(CompiledSizeof expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledBinaryOperatorCall expression, IRBuilder builder)
    {
        IRValue left = EmitExpression(expression.Left, builder);
        IRValue right = EmitExpression(expression.Right, builder);

        IROperatorOp op = expression.Operator switch
        {
            "+" => IROperatorOp.Add,
            "-" => IROperatorOp.Subtract,
            "/" => IROperatorOp.Divide,
            "*" => IROperatorOp.Multiply,
            "%" => IROperatorOp.Modulo,
            "<<" => IROperatorOp.BitshiftLeft,
            ">>" => IROperatorOp.BitshiftRight,
            "&" => IROperatorOp.BitwiseAnd,
            "|" => IROperatorOp.BitwiseOr,
            "^" => IROperatorOp.BitwiseXor,
            "&&" => IROperatorOp.LogicAnd,
            "||" => IROperatorOp.LogicOr,
            ">" => IROperatorOp.CompareGT,
            ">=" => IROperatorOp.CompareGEQ,
            "<" => IROperatorOp.CompareLT,
            "</" => IROperatorOp.CompareLEQ,
            "==" => IROperatorOp.CompareEQ,
            "!=" => IROperatorOp.CompareNEQ,
            _ => throw new NotImplementedException(expression.Operator),
        };

        IRTemporary target = builder.NewTemporary(expression.Type);
        builder.Statements.Add(new IROperator()
        {
            Target = target,
            Left = left,
            Right = right,
            Operator = op,
        });
        return target;
    }
    IRValue EmitExpression(CompiledUnaryOperatorCall expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledConstantValue expression, IRBuilder _)
    {
        return new IRConstant(expression.Type)
        {
            Value = expression.Value,
        };
    }
    IRValue EmitExpression(CompiledRegisterAccess expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledVariableAccess expression, IRBuilder builder)
    {
        IRLocal local = builder.Locals[expression.Variable.Identifier];
        if (local.Values.Count == 0)
        {
            throw new NotImplementedException();
        }
        else if (local.Values.Count == 1)
        {
            return local.Values[0].Temporary;
        }
        else
        {
            IRTemporary temp = builder.NewTemporary(expression.Type);
            foreach (IRLocalVersion item in local.Values)
            {
                if (item.Block is null) continue;
                if (!builder.UnfinishedBlocks.Contains(item.Block)) continue;
                item.Block.Statements = item.Block.Statements.Add(new IRAssignment()
                {
                    Target = temp,
                    Value = item.Temporary,
                });
            }
            return temp;
        }
    }
    IRValue EmitExpression(CompiledExpressionVariableAccess expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledParameterAccess expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledFunctionReference expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledLabelReference expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledFieldAccess expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledElementAccess expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledGetReference expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledDereference expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledStackAllocation expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledConstructorCall expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledCast expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledReinterpretation expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledRuntimeCall expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledFunctionCall expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledExternalFunctionCall expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledString expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledStackString expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledLambda expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledCompilerVariableAccess expression, IRBuilder builder) { throw new NotImplementedException(); }
    IRValue EmitExpression(CompiledExpression expression, IRBuilder builder) => expression switch
    {
        CompiledSizeof v => EmitExpression(v, builder),
        CompiledBinaryOperatorCall v => EmitExpression(v, builder),
        CompiledUnaryOperatorCall v => EmitExpression(v, builder),
        CompiledConstantValue v => EmitExpression(v, builder),
        CompiledRegisterAccess v => EmitExpression(v, builder),
        CompiledVariableAccess v => EmitExpression(v, builder),
        CompiledExpressionVariableAccess v => EmitExpression(v, builder),
        CompiledParameterAccess v => EmitExpression(v, builder),
        CompiledFunctionReference v => EmitExpression(v, builder),
        CompiledLabelReference v => EmitExpression(v, builder),
        CompiledFieldAccess v => EmitExpression(v, builder),
        CompiledElementAccess v => EmitExpression(v, builder),
        CompiledGetReference v => EmitExpression(v, builder),
        CompiledDereference v => EmitExpression(v, builder),
        CompiledStackAllocation v => EmitExpression(v, builder),
        CompiledConstructorCall v => EmitExpression(v, builder),
        CompiledCast v => EmitExpression(v, builder),
        CompiledReinterpretation v => EmitExpression(v, builder),
        CompiledRuntimeCall v => EmitExpression(v, builder),
        CompiledFunctionCall v => EmitExpression(v, builder),
        CompiledExternalFunctionCall v => EmitExpression(v, builder),
        CompiledDummyExpression => throw new NotImplementedException(),
        CompiledString v => EmitExpression(v, builder),
        CompiledStackString v => EmitExpression(v, builder),
        CompiledLambda v => EmitExpression(v, builder),
        CompiledCompilerVariableAccess v => EmitExpression(v, builder),
        _ => throw new NotImplementedException($"Unimplemented expression \"{expression.GetType().Name}\""),
    };

    void EmitStatement(CompiledExpression statement, IRBuilder builder) { throw new NotImplementedException(); }
    void EmitStatement(CompiledVariableDefinition statement, IRBuilder builder)
    {
        IRLocal local = new();

        if (statement.InitialValue is not null)
        {
            IRValue value = EmitExpression(statement.InitialValue, builder);
            IRTemporary temp = builder.NewTemporary(statement.Type);
            builder.Statements.Add(new IRAssignment()
            {
                Target = temp,
                Value = value,
            });

            IRLocalVersion newVersion = new()
            {
                Temporary = temp,
                Block = null,
            };
            builder.UnfinishedLocals.Add(newVersion);
            local.Values.Add(newVersion);
        }

        builder.Locals.Add(statement.Identifier, local);
    }
    void EmitStatement(CompiledReturn statement, IRBuilder builder)
    {
        if (statement.Value is not null)
        {
            IRValue v = EmitExpression(statement.Value, builder);
            builder.Statements.Add(new IRReturn()
            {
                Value = v,
            });
        }
        else
        {
            builder.Statements.Add(new IRReturn());
        }
    }
    void EmitStatement(CompiledCrash statement, IRBuilder builder) { throw new NotImplementedException(); }
    void EmitStatement(CompiledBreak statement, IRBuilder builder) { throw new NotImplementedException(); }
    void EmitStatement(CompiledDelete statement, IRBuilder builder) { throw new NotImplementedException(); }
    void EmitStatement(CompiledGoto statement, IRBuilder builder) { throw new NotImplementedException(); }
    void EmitStatement(CompiledSetter statement, IRBuilder builder)
    {
        IRTemporary target;
        IRValue value;

        switch (statement.Target)
        {
            case CompiledVariableAccess v:
                value = EmitExpression(statement.Value, builder);

                target = builder.NewTemporary(value.Type);
                IRLocal local = builder.Locals[v.Variable.Identifier];

                IRLocalVersion newVersion = new()
                {
                    Temporary = target,
                    Block = null,
                };
                builder.UnfinishedLocals.Add(newVersion);
                local.Values.Add(newVersion);
                break;
            default:
                throw new NotImplementedException(statement.Target.GetType().Name);
        }

        builder.Statements.Add(new IRAssignment()
        {
            Target = target,
            Value = value,
        });
    }
    void EmitStatement(CompiledWhileLoop statement, IRBuilder builder)
    {
        IRSimpleBlock previousBlock = new(builder.NextBlockId(), builder.CompileStatements());
        builder.FinishLocals(previousBlock);
        builder.FinishBlocks(previousBlock);

        IRValue condition = EmitExpression(statement.Condition, builder);
        ImmutableArray<IRStatement> conditionStatements = builder.CompileStatements();

        EmitStatement(statement.Body, builder);
        IRSimpleBlock trueBranch = new(builder.NextBlockId(), builder.CompileStatements());
        builder.FinishLocals(trueBranch);

        IRSimpleBlock falseBranch = new(builder.NextBlockId(), ImmutableArray<IRStatement>.Empty);

        IRBranch branch = new(builder.NextBlockId(), conditionStatements)
        {
            Condition = condition,
            True = trueBranch,
            False = falseBranch,
        };
        builder.FinishLocals(branch);

        previousBlock.Next = branch;
        trueBranch.Next = branch;

        builder.UnfinishedBlocks.Add(falseBranch);
    }
    void EmitStatement(CompiledForLoop statement, IRBuilder builder) { throw new NotImplementedException(); }
    void EmitStatement(CompiledIf statement, IRBuilder builder)
    {
        IRValue condition = EmitExpression(statement.Condition, builder);

        ImmutableArray<IRStatement> previousStatements = builder.CompileStatements();

        EmitStatement(statement.Body, builder);
        IRSimpleBlock trueBranch = new(builder.NextBlockId(), builder.CompileStatements());
        builder.FinishLocals(trueBranch);

        IRSimpleBlock falseBranch;

        if (statement.Next is not null)
        {
            switch (statement.Next)
            {
                case CompiledIf v: EmitStatement(v, builder); break;
                case CompiledElse v: EmitStatement(v.Body, builder); break;
                default: throw new UnreachableException();
            }
            falseBranch = new IRSimpleBlock(builder.NextBlockId(), builder.CompileStatements());
            builder.FinishLocals(falseBranch);
        }
        else
        {
            falseBranch = new(builder.NextBlockId(), ImmutableArray<IRStatement>.Empty);
        }

        IRBranch block = new(builder.NextBlockId(), previousStatements)
        {
            Condition = condition,
            True = trueBranch,
            False = falseBranch,
        };
        builder.FinishLocals(block);
        builder.FinishBlocks(block);

        builder.UnfinishedBlocks.Add(trueBranch);
        builder.UnfinishedBlocks.Add(falseBranch);

    }
    void EmitStatement(CompiledBlock statement, IRBuilder builder)
    {
        foreach (CompiledStatement item in statement.Statements)
        {
            EmitStatement(item, builder);
        }
    }
    void EmitStatement(CompiledLabelDeclaration statement, IRBuilder builder) { throw new NotImplementedException(); }
    void EmitStatement(CompiledStatement statement, IRBuilder builder)
    {
        switch (statement)
        {
            case CompiledExpression v: EmitStatement(v, builder); break;
            case CompiledVariableDefinition v: EmitStatement(v, builder); break;
            case CompiledReturn v: EmitStatement(v, builder); break;
            case CompiledCrash v: EmitStatement(v, builder); break;
            case CompiledBreak v: EmitStatement(v, builder); break;
            case CompiledDelete v: EmitStatement(v, builder); break;
            case CompiledGoto v: EmitStatement(v, builder); break;
            case CompiledSetter v: EmitStatement(v, builder); break;
            case CompiledWhileLoop v: EmitStatement(v, builder); break;
            case CompiledForLoop v: EmitStatement(v, builder); break;
            case CompiledIf v: EmitStatement(v, builder); break;
            case CompiledBlock v: EmitStatement(v, builder); break;
            case CompiledLabelDeclaration v: EmitStatement(v, builder); break;
            case CompiledEmptyStatement: break;
            default: throw new NotImplementedException($"Unimplemented statement \"{statement.GetType().Name}\"");
        }
    }

    IRSimpleBlock Generate()
    {
        IRBuilder builder = new();
        IRSimpleBlock root = new(builder.NextBlockId(), ImmutableArray<IRStatement>.Empty);
        builder.UnfinishedBlocks.Add(root);

        foreach (CompiledStatement statement in CompilerResult.Statements)
        {
            EmitStatement(statement, builder);
        }

        IRSimpleBlock end = new(builder.NextBlockId(), builder.CompileStatements());
        builder.FinishLocals(end);
        builder.FinishBlocks(end);

        HashSet<int> printedBlocks = new();

        static void PrintBlock(IRBlock? block, HashSet<int> printedBlocks)
        {
            switch (block)
            {
                case IRSimpleBlock v: PrintSimpleBlock(v, printedBlocks); break;
                case IRBranch v: PrintBranch(v, printedBlocks); break;
                default: break;
            }
        }

        static void PrintBranch(IRBranch branch, HashSet<int> printedBlocks)
        {
            if (!printedBlocks.Add(branch.Id)) return;

            Debug.WriteLine(null);
            Debug.WriteLine($"{branch.Id}:");

            foreach (IRStatement item in branch.Statements)
            {
                Debug.WriteLine(item.ToString());
            }

            Debug.WriteLine($"if {branch.Condition} -> {branch.True.Id}");
            Debug.WriteLine($"else -> {branch.False.Id}");

            PrintBlock(branch.True, printedBlocks);
            PrintBlock(branch.False, printedBlocks);
        }

        static void PrintSimpleBlock(IRSimpleBlock block, HashSet<int> printedBlocks)
        {
            if (!printedBlocks.Add(block.Id)) return;

            Debug.WriteLine(null);
            Debug.WriteLine($"{block.Id}:");

            foreach (IRStatement item in block.Statements)
            {
                Debug.WriteLine(item.ToString());
            }

            Debug.WriteLine($"-> {block.Next?.Id.ToString() ?? "null"}");

            PrintBlock(block.Next, printedBlocks);
        }

        PrintSimpleBlock(root, printedBlocks);

        return root;
    }

    public static IRSimpleBlock Generate(CompilerResult compilerResult) => new IRGenerator(compilerResult).Generate();
}
