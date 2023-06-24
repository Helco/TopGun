﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TopGun;

public partial class ScriptDecompiler
{
    private readonly byte[] script;
    private readonly int globalVarCount;
    private readonly ResourceFile resFile;

    private List<ASTInstruction> instructions = new List<ASTInstruction>();

    public ScriptDecompiler(ReadOnlySpan<byte> script, ResourceFile resFile)
    {
        this.script = script.ToArray();
        this.resFile = resFile;
        globalVarCount = 5118;
    }

    public void DecompileCalcAndPrintAll(TextWriter textWriter, int indent = 0)
    {
        using var codeWriter = new CodeWriter(textWriter, indent, disposeWriter: false);
        CreateInitialAST();
        TransformCalcReturns();
        instructions.ForEach(i => i.WriteTo(codeWriter));
    }

    private void CreateInitialAST()
    {
        instructions.Clear();
        var rootReader = new SpanReader(script);
        while (!rootReader.EndOfSpan)
        {
            var rootInstruction = new ScriptRootInstruction(ref rootReader);
            instructions.Add(new ASTRootOpInstruction()
            {
                RootInstruction = rootInstruction,
                CalcBody = CreateInitialCalcAST(rootInstruction.Data, rootInstruction.DataOffset),
                StartOwnOffset = rootInstruction.Offset,
                EndOwnOffset = rootInstruction.EndOffset
            });
        }
    }

    private IReadOnlyList<ASTInstruction> CreateInitialCalcAST(ReadOnlySpan<byte> calcScript, int baseOffset)
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
                        .ToArray();
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
                        .ToArray();
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
            entry.Finalize(finalizeOutput!.Count);
            finalizeOutput.Add(entry);
        }
    }
}
