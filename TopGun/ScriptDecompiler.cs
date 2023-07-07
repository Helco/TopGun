using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TopGun;

public partial class ScriptDecompiler
{
    private readonly byte[] script;
    private readonly int globalVarCount;
    private readonly ResourceFile resFile;
    private readonly ASTExitBlock astExit;

    private ASTBlock ASTEntry => blocksByOffset[0];

    private DominanceTree preDominance = null!;
    private DominanceTree postDominance = null!;
    private int nextTmpIndex = 0;
    private Dictionary<int, ASTBlock> blocksByOffset = new();

    public ScriptDecompiler(ReadOnlySpan<byte> script, ResourceFile resFile)
    {
        this.script = script.ToArray();
        this.resFile = resFile;
        astExit = new()
        {
            StartOwnOffset = script.Length,
            EndOwnOffset = script.Length,
            BlocksByOffset = blocksByOffset
        };
        globalVarCount = 5118;
    }

    public void DecompileCalcAndPrintAll(TextWriter textWriter, int indent = 0)
    {
        using var codeWriter = new CodeWriter(textWriter, indent, disposeWriter: false);
        CreateInitialAST();
        TransformCalcLazyBooleans();
        TransformCalcReturns();

        // Control flow analysis
        CreateInitialBlocks();
        SetBlockEdges();
        DebugPrintBlockEdges();
        preDominance = new DominanceTree(new ForwardBlockIterator(ASTEntry));
        postDominance = new DominanceTree(new BackwardBlockIterator(astExit));
        
        // Constructs
        var loops = DetectLoops();
        var selections = DetectSelections();
        var constructs = GroupingConstruct.CreateHierarchy(loops.Concat(selections));
        foreach (var construct in constructs.SelectMany(c => c.AllChildren))
            construct.Construct();
        DebugPrintConstructHierarchy(constructs);
        ConstructContinues();

        WriteTo(codeWriter);

        var unwrittenBlocks = blocksByOffset.Values.Where(b => b is not ASTExitBlock && b.StartTextPosition == default && b.EndTextPosition == default);
        if (unwrittenBlocks.Any())
            throw new Exception($"Detected unwritten blocks ({string.Join(", ", unwrittenBlocks)}), something went wrong");
    }

    private void DebugPrintBlockEdges()
    {
        foreach (var block in blocksByOffset.Values)
        {
            foreach (var outBlock in block.Outbound)
                Console.WriteLine($"{block.StartTotalOffset} -> {outBlock.StartTotalOffset};");
        }
    }

    private void DebugPrintConstructHierarchy(IEnumerable<GroupingConstruct> rootConstructs)
    {
        using var writer = new CodeWriter(Console.Out, disposeWriter: false);
        foreach (var construct in rootConstructs)
            Print(writer, construct);

        static void Print(CodeWriter writer, GroupingConstruct construct)
        {
            writer.WriteLine(construct.GetType().Name +
                " @ " + construct.Body.Min(b => b.StartTotalOffset) +
                " -> " + construct.Body.Max(b => b.EndTotalOffset));
            using var subWriter = writer.Indented;
            foreach (var child in construct.Children.OrderBy(c => c.Body.Min(b => b.StartTotalOffset)))
                Print(subWriter, child);
        }
    }

    private void WriteTo(CodeWriter writer) => ASTEntry.WriteTo(writer);

    private void CreateInitialAST()
    {
        var astEntry = new ASTNormalBlock() { BlocksByOffset = blocksByOffset };
        blocksByOffset.Clear();
        blocksByOffset.Add(astExit.StartTotalOffset, astExit);
        blocksByOffset.Add(0, astEntry);
        var rootReader = new SpanReader(script);
        while (!rootReader.EndOfSpan)
        {
            var rootInstruction = new ScriptRootInstruction(ref rootReader);
            var calcBody = CreateInitialCalcAST(rootInstruction.Data, rootInstruction.DataOffset);

            var astInstruction = new ASTRootOpInstruction()
            {
                Parent = astEntry.Parent,
                RootInstruction = rootInstruction,
                CalcBody = calcBody,
                StartOwnOffset = rootInstruction.Offset,
                EndOwnOffset = rootInstruction.EndOffset
            };

            astEntry.Instructions.Add(astInstruction);
        }
        astEntry.FixChildrenParents();
    }

    private List<ASTInstruction> CreateInitialCalcAST(ReadOnlySpan<byte> calcScript, int baseOffset)
    {
        var output = new List<ASTInstruction>();
        var finalizeOutput = new List<CalcStackEntry>();
        var stack = new List<CalcStackEntry>();
        var reader = new SpanReader(calcScript);
        while (!reader.EndOfSpan)
        {
            var calcInstr = new ScriptCalcInstruction(ref reader, baseOffset);
            switch (calcInstr.Op)
            {
                case ScriptCalcOp.Exit:
                    FinalizeUnusedStack();
                    break;
                case ScriptCalcOp.PushValue:
                    Push(calcInstr, new ASTImmediate { Value = calcInstr.Args[0].Value });
                    break;
                case ScriptCalcOp.PushVar:
                {
                    var index = calcInstr.Args[0].Value;
                    Push(calcInstr, index < globalVarCount
                        ? new ASTGlobalVarValue { Index = index }
                        : new ASTLocalVarValue { Index = index - globalVarCount });
                }
                break;
                case ScriptCalcOp.PushVarAddress:
                {
                    var index = calcInstr.Args[0].Value;
                    Push(calcInstr, index < globalVarCount
                        ? new ASTGlobalVarAddress { Index = index }
                        : new ASTLocalVarAddress { Index = index - globalVarCount });
                }
                break;
                case ScriptCalcOp.ReadVarArray:
                {
                    var index = Pop();
                    var array = Pop();
                    Push(calcInstr, new ASTArrayAccess { Array = array, Index = index });
                }
                break;
                case ScriptCalcOp.ReadVar:
                {
                    var address = Pop();
                    Push(calcInstr, new ASTUnary { Op = UnaryOp.Dereference, Value = address });
                }break;
                case ScriptCalcOp.PushVarValue:
                {
                    var address = Pop();
                    PushEntry(address);
                    Push(calcInstr, new ASTUnary { Op = UnaryOp.Dereference, Value = address });
                }
                break;
                case ScriptCalcOp.OffsetVar:
                {
                    var offset = Pop();
                    var address = Pop();
                    Push(calcInstr, new ASTBinary { Op = BinaryOp.Add, Left = address, Right = offset });
                }break;
                case ScriptCalcOp.WriteVar:
                {
                    var value = Pop();
                    output.Add(new ASTAssign
                    {
                        Value = value,
                        Address = Pop(),
                        StartOwnOffset = calcInstr.Offset,
                        EndOwnOffset = calcInstr.EndOffset
                    });
                    PushEntry(value);
                }break;
                case ScriptCalcOp.CallProc:
                {
                    var localScopeSize = calcInstr.Args[0].Value;
                    var argCount = calcInstr.Args[1].Value;
                    var args = Enumerable
                        .Repeat(0, argCount)
                        .Select(_ => Pop())
                        .Reverse()
                        .ToList();
                    var procId = Pop();
                    if (procId.ValueExpression is ASTImmediate immProcId)
                    {
                        var procIdValue = immProcId.Value;
                        if (procIdValue > resFile.MaxScrMsg)
                        {
                            var extProcIdx = (int)(procIdValue - resFile.MaxScrMsg - 1);
                            if (extProcIdx < resFile.PluginProcs.Count)
                            {
                                var (plugin, localProcIdx) = resFile.PluginProcs[extProcIdx];
                                Push(calcInstr, new ASTExternalProcCall { Plugin = plugin.Name, Proc = plugin.Procs[localProcIdx], Args = args, LocalScopeSize = localScopeSize });
                            }
                            else
                                Push(calcInstr, new ASTUnknownExternalProcCall { ProcId = extProcIdx, Args = args, LocalScopeSize = localScopeSize });
                        }
                        else
                            Push(calcInstr, new ASTInternalProcCall { ProcId = procIdValue, Args = args, LocalScopeSize = localScopeSize });
                        stack.Last().ValueExpression.StartOwnOffset = immProcId.StartOwnOffset;
                    }
                    else
                        Push(calcInstr, new ASTDynamicProcCall { ProcId = procId, Args = args, LocalScopeSize = localScopeSize });
                }break;
                case ScriptCalcOp.RunScript:
                {
                    var localScopeSize = calcInstr.Args[0].Value;
                    var argCount = calcInstr.Args[1].Value;
                    var args = Enumerable
                        .Repeat(0, argCount)
                        .Select(_ => Pop())
                        .Reverse()
                        .ToList();
                    var procId = Pop();
                    Push(calcInstr, new ASTScriptCall { ScriptIndex = procId, Args = args, LocalScopeSize = localScopeSize });
                }
                break;
                case ScriptCalcOp.Negate: Push(calcInstr, new ASTUnary { Op = UnaryOp.Negate, Value = Pop() }); break;
                case ScriptCalcOp.BooleanNot: Push(calcInstr, new ASTUnary { Op = UnaryOp.BooleanNot, Value = Pop() }); break;
                case ScriptCalcOp.BitNot: Push(calcInstr, new ASTUnary { Op = UnaryOp.BitNot, Value = Pop() }); break;
                case ScriptCalcOp.Add: Push(calcInstr, new ASTBinary { Op = BinaryOp.Add, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Sub: Push(calcInstr, new ASTBinary { Op = BinaryOp.Subtract, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Mul: Push(calcInstr, new ASTBinary { Op = BinaryOp.Multiply, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Div: Push(calcInstr, new ASTBinary { Op = BinaryOp.Divide, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Mod: Push(calcInstr, new ASTBinary { Op = BinaryOp.Modulo, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Equals: Push(calcInstr, new ASTBinary { Op = BinaryOp.Equals, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.LessOrEquals: Push(calcInstr, new ASTBinary { Op = BinaryOp.LessOrEquals, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Less: Push(calcInstr, new ASTBinary { Op = BinaryOp.Lesser, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.GreaterOrEquals: Push(calcInstr, new ASTBinary { Op = BinaryOp.GreaterOrEquals, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Greater: Push(calcInstr, new ASTBinary { Op = BinaryOp.Greater, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.NotEquals: Push(calcInstr, new ASTBinary { Op = BinaryOp.NotEquals, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.BooleanAnd: Push(calcInstr, new ASTBinary { Op = BinaryOp.EvalBooleanAnd, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.BooleanOr: Push(calcInstr, new ASTBinary { Op = BinaryOp.EvalBooleanOr, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.BitAnd: Push(calcInstr, new ASTBinary { Op = BinaryOp.BitAnd, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.BitOr: Push(calcInstr, new ASTBinary { Op = BinaryOp.BitOr, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.BitXor: Push(calcInstr, new ASTBinary { Op = BinaryOp.BitXor, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.ShiftLeft: Push(calcInstr, new ASTBinary { Op = BinaryOp.ShiftLeft, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.ShiftRight: Push(calcInstr, new ASTBinary { Op = BinaryOp.ShiftRight, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.PreIncrementVar: Push(calcInstr, new ASTUnary { Op = UnaryOp.PreIncrement, Value = Pop() }); break;
                case ScriptCalcOp.PostIncrementVar: Push(calcInstr, new ASTUnary { Op = UnaryOp.PostIncrement, Value = Pop() }); break;
                case ScriptCalcOp.PreDecrementVar: Push(calcInstr, new ASTUnary { Op = UnaryOp.PreDecrement, Value = Pop() }); break;
                case ScriptCalcOp.PostDecrementVar: Push(calcInstr, new ASTUnary { Op = UnaryOp.PostDecrement, Value = Pop() }); break;

                case ScriptCalcOp.JumpZero:
                case ScriptCalcOp.JumpNonZero:
                {
                    var condition = Pop();
                    PushEntry(condition);
                    output.Add(new ASTConditionalCalcJump
                    {
                        Zero = calcInstr.Op == ScriptCalcOp.JumpZero,
                        Condition = condition,
                        Target = calcInstr.Offset + 1 + calcInstr.Args[0].Value,
                        StartOwnOffset = calcInstr.Offset,
                        EndOwnOffset = calcInstr.EndOffset
                    });
                }break;

                default: throw new NotSupportedException($"Decompiler does not support calc op {calcInstr.Op}");
            }
        }

        FinalizeUnusedStack();
        foreach (var entry in (finalizeOutput as IEnumerable<CalcStackEntry>).Reverse())
        {
            output.Insert(entry.FinalizeInsert, new ASTTmpDeclaration
            {
                Index = entry.FinalizeIndex,
                Value = entry.ValueExpression,
                StartOwnOffset = entry.ValueExpression.StartOwnOffset,
                EndOwnOffset = entry.ValueExpression.EndOwnOffset
            });
        }

        return output;

        void PushEntry(CalcStackEntry entry) => stack!.Add(entry);
        void Push(ScriptCalcInstruction instr, ASTExpression expression)
        {
            expression.StartOwnOffset = instr.Offset;
            expression.EndOwnOffset = instr.EndOffset;
            PushEntry(new CalcStackEntry(expression, output!.Count));
        }

        CalcStackEntry Pop()
        {
            var entry = stack!.RemoveLast();
            entry.RefCount++;
            if (entry.RefCount == 2)
                FinalizeEntry(entry);
            return entry;
        }

        void FinalizeUnusedStack()
        {
            foreach (var entry in stack.Where(e => e.RefCount == 0))
                FinalizeEntry(entry, evenConstants: true);
        }

        void FinalizeEntry(CalcStackEntry entry, bool evenConstants = false)
        {
            if (entry.ValueExpression.IsConstant && !evenConstants)
                return;
            entry.RefCount = 2;
            entry.Finalize(nextTmpIndex++);
            finalizeOutput.Add(entry);
        }
    }

    private ASTExpression ExpressionFromRootArg(ScriptRootInstruction.Arg arg) => arg.Type switch
    {
        ScriptRootInstruction.ArgType.Value => new ASTImmediate() { Value = arg.Value},
        ScriptRootInstruction.ArgType.Indirect when arg.Value < globalVarCount => new ASTGlobalVarValue() { Index = arg.Value },
        ScriptRootInstruction.ArgType.Indirect => new ASTLocalVarValue() { Index = arg.Value - globalVarCount },
        _ => throw new NotSupportedException("Unsupported root argument for conversion to AST expression")
    };

    private ASTExpression ExpressionFromConditionOp(ScriptRootInstruction.Arg leftArg, ScriptRootInstruction.Arg rightArg, ScriptRootConditionOp op, bool negateByInstruction)
    {
        BinaryOp? binary = null;
        var shouldNegate = negateByInstruction;
        if (op.HasFlag(ScriptRootConditionOp.Negate))
            shouldNegate = !shouldNegate;

        if (op.HasFlag(ScriptRootConditionOp.Equals))
            (binary, shouldNegate) = (shouldNegate ? BinaryOp.NotEquals : BinaryOp.Equals, false);
        else if (op.HasFlag(ScriptRootConditionOp.Greater))
            (binary, shouldNegate) = (shouldNegate ? BinaryOp.LessOrEquals : BinaryOp.Greater, false);
        else if (op.HasFlag(ScriptRootConditionOp.Lesser))
            (binary, shouldNegate) = (shouldNegate ? BinaryOp.GreaterOrEquals : BinaryOp.Lesser, false);
        else if (op.HasFlag(ScriptRootConditionOp.Or))
            binary = BinaryOp.BitOr;
        else if (op.HasFlag(ScriptRootConditionOp.And))
            binary = BinaryOp.BitAnd;
        else if (op.HasFlag(ScriptRootConditionOp.Modulo))
            binary = BinaryOp.Modulo;
        // ignoring NotZero as no op is necessary

        var expr = binary == null ? ExpressionFromRootArg(leftArg) : new ASTBinary()
        {
            Left = new(ExpressionFromRootArg(leftArg), -1),
            Right = new(ExpressionFromRootArg(rightArg), -1),
            Op = binary.Value
        };
        if (shouldNegate)
            expr = new ASTUnary()
            {
                Value = new(expr, -1),
                Op = UnaryOp.BooleanNot
            };
        return expr;
    }
}
