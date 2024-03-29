using System;
using System.Collections.Generic;
using System.Linq;

namespace TopGun;

partial class ScriptDecompiler
{
    private void TransformConstructCalcBlockExpressions()
    {
        foreach (var construct in constructs.SelectMany(c => c.AllChildren))
        {
            switch (construct.Result)
            {
                case ASTLoop loop:
                    loop.ConditionExpression = TryExtractExpression(loop.Condition);
                    break;
                case ASTIfElse ifElse:
                    ifElse.ConditionExpression = TryExtractExpression(ifElse.Condition);
                    break;
                case ASTSwitch @switch:
                    @switch.ValueExpression = TryExtractExpression(@switch.Value);
                    break;
            }
        }

        static ASTExpression? TryExtractExpression(ASTBlock block)
        {
            if (block is not ASTNormalBlock normalBlock ||
                normalBlock.Instructions.Count > 1 ||
                normalBlock.Instructions.Single() is not ASTRootOpInstruction astRootOp ||
                astRootOp.CalcBody.Count > 1 ||
                astRootOp.CalcBody.Single() is not ASTReturn astReturn)
                return null;
            block.IsReplacedByNonBlock = true;
            return astReturn.Value;
        }

        var reducibleRootReturns = ASTEntry.AllChildren
            .OfType<ASTRootOpInstruction>()
            .Where(i => i.RootInstruction.Op == ScriptOp.Return && 
                i.CalcBody.Count == 1 &&
                i.CalcBody.Single() is ASTReturn)
            .ToArray();
        foreach (var astRootReturn in reducibleRootReturns)
            astRootReturn.ReplaceMeWith(astRootReturn.CalcBody.Single());
    }
}
