using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace TopGun;

public partial class ScriptDecompiler
{
    private readonly byte[] script;
    private readonly int globalVarCount;
    private readonly ResourceFile resFile;

    public ScriptDecompiler(ReadOnlySpan<byte> script, ResourceFile resFile)
    {
        this.script = script.ToArray();
        this.resFile = resFile;
        globalVarCount = 5118;
    }

    public void Decompile(TextWriter writer, int indent = 0)
    {
        var reader = new SpanReader(script);
        while (!reader.EndOfSpan)
        {
            var rootInstruction = new ScriptRootInstruction(ref reader);
            WriteIndent(writer, indent);
            writer.Write(rootInstruction.ToStringWithoutData());

            if (rootInstruction.Op == ScriptRootOp.ComplexCalc)
            {
                writer.WriteLine();
                while (rootInstruction.Op == ScriptRootOp.ComplexCalc)
                {
                    DecompileCalc(writer, rootInstruction.Data, indent + 1);
                    if (reader.EndOfSpan)
                        return;
                    rootInstruction = new ScriptRootInstruction(ref reader);
                }

                WriteIndent(writer, indent);
                writer.Write(rootInstruction.ToStringWithoutData());
            }

            if (rootInstruction.Data.IsEmpty)
            {
                writer.WriteLine();
                continue;
            }

            WriteIndent(writer, indent);
            writer.WriteLine("{");
            DecompileCalc(writer, rootInstruction.Data, indent + 1);
            WriteIndent(writer, indent);
            writer.WriteLine("}");
        }
    }

    private void DecompileCalc(TextWriter writer, ReadOnlySpan<byte> data, int indent)
    {
        //var reader = new SpanReader(data);
        //while (!reader.EndOfSpan)
            //writer.WriteLine(new ScriptCalcInstruction(ref reader));

        var instructions = DecompileCalc(data);
        foreach (var instruction in instructions)
            instruction.WriteTo(writer, indent);
    }

    private IReadOnlyList<ASTInstruction> DecompileCalc(ReadOnlySpan<byte> calcScript)
    {
        var output = new List<ASTInstruction>();
        var finalizeOutput = new List<CalcStackEntry>();
        var stack = new List<CalcStackEntry>();
        var reader = new SpanReader(calcScript);
        while (!reader.EndOfSpan)
        {
            var calcInstr = new ScriptCalcInstruction(ref reader);
            switch (calcInstr.Op)
            {
                case ScriptCalcOp.Exit:
                    FinalizeUnusedStack();
                    break;
                case ScriptCalcOp.PushValue:
                    Push(new ASTImmediate { Value = calcInstr.Args[0].Value });
                    break;
                case ScriptCalcOp.PushVar:
                {
                    var index = calcInstr.Args[0].Value;
                    Push(index < globalVarCount
                        ? new ASTGlobalVarValue { Index = index }
                        : new ASTLocalVarValue { Index = index - globalVarCount });
                }
                break;
                case ScriptCalcOp.PushVarAddress:
                {
                    var index = calcInstr.Args[0].Value;
                    Push(index < globalVarCount
                        ? new ASTGlobalVarAddress { Index = index }
                        : new ASTLocalVarAddress { Index = index - globalVarCount });
                }
                break;
                case ScriptCalcOp.ReadVarArray:
                {
                    var index = Pop();
                    var array = Pop();
                    Push(new ASTArrayAccess { Array = array, Index = index });
                }
                break;
                case ScriptCalcOp.ReadVar:
                {
                    var address = Pop();
                    Push(new ASTUnary { Op = UnaryOp.Dereference, Value = address });
                }break;
                case ScriptCalcOp.PushVarValue:
                {
                    var address = Pop();
                    PushEntry(address);
                    Push(new ASTUnary { Op = UnaryOp.Dereference, Value = address });
                }
                break;
                case ScriptCalcOp.OffsetVar:
                {
                    var offset = Pop();
                    var address = Pop();
                    Push(new ASTBinary { Op = BinaryOp.Add, Left = address, Right = offset });
                }break;
                case ScriptCalcOp.WriteVar:
                {
                    var value = Pop();
                    var address = Pop();
                    output.Add(new ASTAssign { Address = address, Value = value });
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
                        if (procIdValue >= resFile.MaxScrMsg)
                        {
                            var extProcIdx = (int)(procIdValue - resFile.MaxScrMsg);
                            if (extProcIdx < resFile.PluginProcs.Count)
                            {
                                var (plugin, localProcIdx) = resFile.PluginProcs[extProcIdx];
                                Push(new ASTExternalProcCall { Plugin = plugin.Name, Proc = plugin.Procs[localProcIdx], Args = args, LocalScopeSize = localScopeSize });
                            }
                            else
                                Push(new ASTUnknownExternalProcCall { ProcId = extProcIdx, Args = args, LocalScopeSize = localScopeSize });
                        }
                        else
                            Push(new ASTInternalProcCall { ProcId = procIdValue, Args = args, LocalScopeSize = localScopeSize });
                    }
                    else
                        Push(new ASTDynamicProcCall { ProcId = procId, Args = args, LocalScopeSize = localScopeSize });
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
                    Push(new ASTScriptCall { ScriptIndex = procId, Args = args, LocalScopeSize = localScopeSize });
                }
                break;
                case ScriptCalcOp.Negate: Push(new ASTUnary { Op = UnaryOp.Negate, Value = Pop() }); break;
                case ScriptCalcOp.BooleanNot: Push(new ASTUnary { Op = UnaryOp.BooleanNot, Value = Pop() }); break;
                case ScriptCalcOp.BitNot: Push(new ASTUnary { Op = UnaryOp.BitNot, Value = Pop() }); break;
                case ScriptCalcOp.Add: Push(new ASTBinary { Op = BinaryOp.Add, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Sub: Push(new ASTBinary { Op = BinaryOp.Subtract, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Mul: Push(new ASTBinary { Op = BinaryOp.Multiply, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Div: Push(new ASTBinary { Op = BinaryOp.Divide, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Mod: Push(new ASTBinary { Op = BinaryOp.Modulo, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Equals: Push(new ASTBinary { Op = BinaryOp.Equals, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.LessOrEquals: Push(new ASTBinary { Op = BinaryOp.LessOrEquals, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Less: Push(new ASTBinary { Op = BinaryOp.Lesser, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.GreaterOrEquals: Push(new ASTBinary { Op = BinaryOp.GreaterOrEquals, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.Greater: Push(new ASTBinary { Op = BinaryOp.Greater, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.NotEquals: Push(new ASTBinary { Op = BinaryOp.NotEquals, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.BooleanAnd: Push(new ASTBinary { Op = BinaryOp.BooleanAnd, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.BooleanOr: Push(new ASTBinary { Op = BinaryOp.BooleanOr, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.BitAnd: Push(new ASTBinary { Op = BinaryOp.BitAnd, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.BitOr: Push(new ASTBinary { Op = BinaryOp.BitOr, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.BitXor: Push(new ASTBinary { Op = BinaryOp.BitXor, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.ShiftLeft: Push(new ASTBinary { Op = BinaryOp.ShiftLeft, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.ShiftRight: Push(new ASTBinary { Op = BinaryOp.ShiftRight, Right = Pop(), Left = Pop() }); break;
                case ScriptCalcOp.PreIncrementVar: Push(new ASTUnary { Op = UnaryOp.PreIncrement, Value = Pop() }); break;
                case ScriptCalcOp.PostIncrementVar: Push(new ASTUnary { Op = UnaryOp.PostIncrement, Value = Pop() }); break;
                case ScriptCalcOp.PreDecrementVar: Push(new ASTUnary { Op = UnaryOp.PreDecrement, Value = Pop() }); break;
                case ScriptCalcOp.PostDecrementVar: Push(new ASTUnary { Op = UnaryOp.PostDecrement, Value = Pop() }); break;

                case ScriptCalcOp.JumpZero:
                case ScriptCalcOp.JumpNonZero:
                {
                    var jumpTarget = calcInstr.Offset + calcInstr.Args[0].Value;
                    var condition = Pop();
                    PushEntry(condition);
                    output.Add(new ASTConditionalJump { Zero = calcInstr.Op == ScriptCalcOp.JumpZero, Condition = condition, Target = jumpTarget });
                }break;

                default: throw new NotSupportedException($"Decompiler does not support calc op {calcInstr.Op}");
            }
        }

        FinalizeUnusedStack();
        foreach (var entry in (finalizeOutput as IEnumerable<CalcStackEntry>).Reverse())
            output.Insert(entry.FinalizeInsert, new ASTTmpDeclaration { Index = entry.FinalizeIndex, Value = entry.ValueExpression });

        return output;

        void PushEntry(CalcStackEntry entry) => stack!.Add(entry);
        void Push(ASTExpression expression) => PushEntry(new CalcStackEntry(expression, output!.Count));

        CalcStackEntry Pop()
        {
            var entry = stack!.RemoveLast();
            if (!entry.ValueExpression.IsConstant)
                entry.RefCount++;
            if (entry.RefCount == 2)
                FinalizeEntry(entry);
            return entry;
        }

        void FinalizeUnusedStack()
        {
            foreach (var entry in stack.Where(e => e.RefCount == 0))
                FinalizeEntry(entry);
        }

        void FinalizeEntry(CalcStackEntry entry)
        {
            if (entry.ValueExpression.IsConstant)
                return;
            entry.RefCount = 2;
            entry.Finalize(finalizeOutput!.Count);
            finalizeOutput.Add(entry);
        }
    }
}
