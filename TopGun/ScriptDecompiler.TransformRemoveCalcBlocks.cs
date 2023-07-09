using System;
using System.Collections.Generic;
using System.Linq;

namespace TopGun;

partial class ScriptDecompiler
{
    private void TransformRemoveCalcBlocks()
    {
        var runCalcInstructions = ASTEntry.AllChildren
            .OfType<ASTRootOpInstruction>()
            .Where(i => i.RootInstruction.Op == ScriptOp.RunCalc)
            .Distinct()
            .ToArray();
        foreach (var astRunCalc in runCalcInstructions)
        {
            var block = (ASTNormalBlock)astRunCalc.Parent!;
            var runCalcI = block.Instructions.IndexOf(astRunCalc);
            block.Instructions.RemoveAt(runCalcI);
            block.Instructions.InsertRange(runCalcI, astRunCalc.CalcBody);
        }
    }
}
