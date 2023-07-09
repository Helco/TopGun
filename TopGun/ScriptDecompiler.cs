using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TopGun;

public partial class ScriptDecompiler
{
    [Flags]
    public enum DebugFlags
    {
        None = 0,
        All = ~0,

        PrintBlockEdges = 1 << 0,
        PrintConstructHierarchy = 1 << 1,
        PrintBlockHierarchy = 1 << 2,
    }

    private readonly byte[] script;
    private readonly int globalVarCount;
    private readonly ResourceFile resFile;
    private readonly ASTExitBlock astExit;

    private DominanceTree preDominance = null!;
    private DominanceTree postDominance = null!;
    private int nextTmpIndex = 0;
    private Dictionary<int, ASTBlock> blocksByOffset = new();
    private HashSet<int> earlyExitOffsets = new();
    private HashSet<(int, int)> controllingEdgesAtExit = new();
    private List<GroupingConstruct> constructs = new();

    private ASTBlock ASTEntry => blocksByOffset[0];
    public DebugFlags Debug { get; set; } = DebugFlags.None;

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

    public void Decompile()
    {
        nextTmpIndex = 0;
        CreateInitialAST();
        TransformCalcLazyBooleans();
        TransformCalcReturns();

        // Control flow analysis
        CreateInitialBlocks();
        SetBlockEdges();
        RemoveUnreachableJumps();
        DebugPrintBlockEdges();
        preDominance = new DominanceTree(new ForwardBlockIterator(ASTEntry));
        postDominance = new DominanceTree(new BackwardBlockIterator(astExit));
        DetectControllingEdgesAtExit();
        if (controllingEdgesAtExit.Any())
            postDominance = new DominanceTree(new EdgeIgnoringBlockIterator()
            {
                Parent = new BackwardBlockIterator(astExit),
                IgnoreEdges = controllingEdgesAtExit
            });
        
        // Constructs (to use results from CFA)
        var loops = DetectLoops();
        var selections = DetectSelections();
        constructs = GroupingConstruct.CreateHierarchy(loops.Concat(selections));
        foreach (var construct in constructs.SelectMany(c => c.AllChildren))
            construct.Construct();
        DebugPrintConstructHierarchy();
        ConstructContinues();
        DebugPrintBlockHierarchy();

        // Clean ups
        TransformConstructCalcBlockExpressions();
    }

    public void WriteTo(TextWriter textWriter, int indent = 0)
    {
        using var codeWriter = new CodeWriter(textWriter, indent, disposeWriter: false);
        WriteTo(codeWriter);
    }

    public void WriteTo(CodeWriter writer)
    {
        foreach (var block in ASTEntry.AllChildren.OfType<ASTBlock>())
            block.ResetTextPosition();

        ASTEntry.WriteTo(writer);

        var unwrittenBlocks = blocksByOffset.Values.Where(b =>
            b is not ASTExitBlock &&
            !b.IsReplacedByNonBlock &&
            b.StartTextPosition == default &&
            b.EndTextPosition == default);
        if (unwrittenBlocks.Any())
            throw new Exception($"Detected unwritten blocks ({string.Join(", ", unwrittenBlocks)}), something went wrong");
    }

    private void DebugPrintBlockEdges()
    {
        if (!Debug.HasFlag(DebugFlags.PrintBlockEdges))
            return;
        foreach (var block in blocksByOffset.Values)
        {
            foreach (var outBlock in block.Outbound)
                Console.WriteLine($"{block.StartTotalOffset} -> {outBlock.StartTotalOffset};");
        }
    }

    private void DebugPrintConstructHierarchy()
    {
        if (!Debug.HasFlag(DebugFlags.PrintConstructHierarchy))
            return;
        using var writer = new CodeWriter(Console.Out, disposeWriter: false);
        foreach (var construct in constructs)
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

    private void DebugPrintBlockHierarchy()
    {
        if (!Debug.HasFlag(DebugFlags.PrintBlockHierarchy))
            return;
        using var writer = new CodeWriter(Console.Out, disposeWriter: false);
        var visited = new HashSet<ASTBlock>();
        Print(writer, ASTEntry);

        void Print(CodeWriter writer, ASTBlock block)
        {
            writer.WriteLine(block.ToString());
            using var subWriter = writer.Indented;
            if (!visited.Add(block))
            {
                subWriter.WriteLine("<DUPLICATED>");
                return;
            }
            var childrenBlocks = block.Children
                .OfType<ASTBlock>()
                .Where(b => b != block.ContinueBlock)
                .OrderBy(b => b.StartTotalOffset);
            foreach (var child in childrenBlocks)
                Print(subWriter, child);
            if (block.ContinueBlock != null)
            {
                writer.Write("cont: ");
                Print(writer, block.ContinueBlock);
            }
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
