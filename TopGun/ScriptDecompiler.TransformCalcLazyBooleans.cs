using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TopGun;

partial class ScriptDecompiler
{
    private void TransformCalcLazyBooleans()
    {
        var potentialOps = astEntry.AllChildren
            .OfType<ASTBinary>()
            .Where(b => b.Op == BinaryOp.EvalBooleanAnd || b.Op == BinaryOp.EvalBooleanOr)
            .Where(b => b.Left.RefExpression is ASTTmpValue)
            .ToArray();

        foreach (var potentialOp in potentialOps)
            TryTransformCalcLazyBoolean(potentialOp);
    }

    private void TryTransformCalcLazyBoolean(ASTBinary potentialOp)
    {
        /*
         * Searching for patterns like
         * tmpX = leftExpr;
         * if/ifnot (tmpX) goto ...; // jump inbetween eval boolean op
         * ... tmpX ||/&& rightExpr ...
         * 
         * And transforming them to lazy boolean ops
         * leftExpr ||?/&&? rightExpr
         */

        var tmpIndex = ((ASTTmpValue)potentialOp.Left.RefExpression).Index;
        var tmpDecl = astEntry.AllChildren
            .OfType<ASTTmpDeclaration>()
            .Single(d => d.Index == tmpIndex);
        var parent = (ASTRootOpInstruction)tmpDecl.Parent!;
        var container = parent.CalcBody;
        var tmpDeclIndex = container.IndexOf(tmpDecl);
        var tmpReferences = container
            .SelectMany(c => c.AllChildren)
            .OfType<ASTTmpValue>()
            .Where(t => t.Index == tmpIndex)
            .ToArray();
        if (tmpDeclIndex + 2 >= container.Count ||
            tmpReferences.Length != 2 ||
            container[tmpDeclIndex + 1] is not ASTConditionalCalcJump calcJump ||
            !container[tmpDeclIndex + 2].AllChildren.Contains(potentialOp) ||
            calcJump.Zero != (potentialOp.Op == BinaryOp.EvalBooleanAnd) ||
            calcJump.Condition.RefExpression != tmpReferences[0] ||
            potentialOp.Left.RefExpression != tmpReferences[1])
            return;

        // now we know that we have the expected pattern and the temporary is not used anywhere else
        // just to be sure, let's also check the jump offset, it should just jump over rightExpr
        if (calcJump.EndTotalOffset != potentialOp.Right.RefExpression.StartTotalOffset ||
            calcJump.Target != potentialOp.Right.RefExpression.EndTotalOffset ||
            calcJump.Target != potentialOp.StartOwnOffset)
            throw new Exception("This should not have happened, please investigate or tell developer to");

        potentialOp.Left = new(tmpDecl.Value, -1);
        potentialOp.Left.ValueExpression.Parent = potentialOp;
        potentialOp.Op = potentialOp.Op == BinaryOp.EvalBooleanAnd
            ? BinaryOp.LazyBooleanAnd
            : BinaryOp.LazyBooleanOr;

        container.RemoveAt(tmpDeclIndex);
        container.RemoveAt(tmpDeclIndex); // the conditional jump
    }
}
