﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TopGun;

internal enum CalcInRootMode
{
    ShouldNotExist,
    ReturnsValue,
    DoesNotReturnValue
}

partial class ScriptDecompiler
{
    private static readonly IReadOnlySet<ScriptOp> ValueReturningOps = new HashSet<ScriptOp>()
    {
        ScriptOp.JumpIfCalc,
        ScriptOp.JumpIfCalc_dup,
        ScriptOp.Return,
        ScriptOp.CalcSwitch
    };

    private static readonly IReadOnlySet<ScriptOp> VoidOps = new HashSet<ScriptOp>()
    {
        ScriptOp.RunCalc
    };

    private static CalcInRootMode GetCalcInRootMode(ScriptOp op) =>
        ValueReturningOps.Contains(op) ? CalcInRootMode.ReturnsValue
        : VoidOps.Contains(op) ? CalcInRootMode.DoesNotReturnValue
        : CalcInRootMode.ShouldNotExist;

    private void TransformCalcReturns()
    {
        foreach (ASTRootOpInstruction instr in astRoot.Instructions)
        {
            var mode = GetCalcInRootMode(instr.RootInstruction.Op);
            if (mode == CalcInRootMode.ShouldNotExist && instr.CalcBody.Any())
                throw new InvalidDataException($"Unexpected calc body for {instr.RootInstruction.Op}");
            else if (mode != CalcInRootMode.ShouldNotExist && !instr.CalcBody.Any())
                throw new InvalidDataException($"Expected calc body for {instr.RootInstruction.Op} but did not have any");
            else if (mode == CalcInRootMode.DoesNotReturnValue)
                TransformNonReturningCalc(instr);
            else if (mode == CalcInRootMode.ReturnsValue)
                TransformReturningCalc(instr);
        }

        void TransformNonReturningCalc(ASTRootOpInstruction instr)
        {
            if (instr.CalcBody.LastOrDefault() is not ASTTmpDeclaration tmpDecl)
                return;
            var exprInstr = new ASTExprInstr()
            {
                Parent = tmpDecl.Parent,
                Expression = tmpDecl.Value
            };
            exprInstr.Expression.Parent = exprInstr;
            tmpDecl.ReplaceMeWith(exprInstr);

        }

        void TransformReturningCalc(ASTRootOpInstruction instr)
        {
            if (instr.CalcBody.LastOrDefault() is not ASTTmpDeclaration tmpDecl)
                return;
            var returnInstr = new ASTReturn()
            {
                Parent = tmpDecl.Parent,
                Value = tmpDecl.Value
            };
            returnInstr.Value.Parent = returnInstr;
            tmpDecl.ReplaceMeWith(returnInstr);
        }
    }
}
