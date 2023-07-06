using System;
using System.Collections.Generic;
using System.Linq;

namespace TopGun;

partial class ScriptDecompiler
{
    private abstract class GroupingConstruct
    {
        public required ScriptDecompiler Decompiler { get; init; }
        public GroupingConstruct? Parent { get; set; }
        public List<GroupingConstruct> Children { get; } = new();
        public HashSet<ASTBlock> Body { get; init; } = new();
        public IEnumerable<GroupingConstruct> AllChildren => Children.SelectMany(c => c.AllChildren).Prepend(this);

        public int Rank
        {
            get 
            {
                int rank = 0;
                var cur = this;
                while(cur != null)
                {
                    rank++;
                    cur = cur.Parent;
                }
                return rank;
            }
        }

        public static int DescendingSizeComparison(GroupingConstruct a, GroupingConstruct b) => b.Body.Count - a.Body.Count;

        public void AddChild(GroupingConstruct child)
        {
            var parentChild = Children.SingleOrDefault(c => c.Body.Overlaps(child.Body));
            if (parentChild == null)
            {
                child.Parent = this;
                Children.Add(child);
                Children.Sort(DescendingSizeComparison);
                return;
            }
            if (!parentChild.Body.IsSupersetOf(child.Body))
                throw new NotSupportedException("Unsupported control flow with overlapping, non-nested construct bodies");
            parentChild.AddChild(child);
        }

        public static List<GroupingConstruct> CreateHierarchy(List<GroupingConstruct> allConstructs)
        {
            var dummyRoot = new DummyConstruct()
            {
                Decompiler = null!,
                Body = null!
            };
            allConstructs.Sort(DescendingSizeComparison);
            allConstructs.ForEach(dummyRoot.AddChild);
            dummyRoot.Children.ForEach(c => c.Parent = null);
            return dummyRoot.Children;
        }

        public abstract void Construct();
    }

    private class DummyConstruct : GroupingConstruct
    {
        // used as a root construct to create the hierarchy
        public override void Construct()
        {
            throw new NotImplementedException("Attempted to construct a dummy construct");
        }
    }

    private class NaturalLoop : GroupingConstruct
    {
        public ASTBlock Header { get; init; } = null!;

        public override void Construct()
        {
            var backTarget = Header;
            var targetInbounds = backTarget.Inbound.ToArray();
            var targetOutbounds = backTarget.Outbound.ToArray();
            if (targetInbounds.Length != 2 || targetOutbounds.Length != 2)
                throw new NotSupportedException("Loop with unexpected structure, header has not two inbound and two outbound blocks");

            var backSource = (ASTNormalBlock)(Body.Contains(targetInbounds[0]) ? targetInbounds[0] : targetInbounds[1]);
            var externalInbound = backSource == targetInbounds[0] ? targetInbounds[1] : targetInbounds[0];
            if (!Body.Contains(backSource) || Body.Contains(externalInbound))
                throw new NotSupportedException("Loop with unexpected structure, header inbounds are not supported");

            if (backSource.Outbound.Count() != 1)
                throw new NotSupportedException("Loop with unexpected structure, back-edge block is branching");
            backSource.ConstructProvidesControlFlow = true;
            backSource.LastInstructionIsRedundantControlFlow = true;
            var jumpBackInstr = ((ASTNormalBlock)backSource).Instructions.RemoveLast();
            if ((jumpBackInstr as ASTRootOpInstruction)?.RootInstruction.Op != ScriptOp.Jump)
                throw new NotSupportedException("Should the back-edge source block not end with an unconditional jump?");
            
            var loopEntry = Body.Contains(targetOutbounds[0]) ? targetOutbounds[0] : targetOutbounds[1];
            var externalOutbound = loopEntry == targetOutbounds[0] ? targetOutbounds[1] : targetOutbounds[0];
            if (!Body.Contains(loopEntry) || Body.Contains(externalOutbound))
                throw new NotSupportedException("Loop with unexpected structure, header outbounds are not supported");

            var astLoop = new ASTLoop()
            {
                BlocksByOffset = Header.BlocksByOffset,
                IsPostCondition = false,
                Condition = backTarget,
                BodyOffset = loopEntry.StartTotalOffset,
                Loop = Body,
                ContinueOffset = externalOutbound?.StartTotalOffset
            };
            foreach (var block in Body)
                block.Parent = astLoop;
            astLoop.BlocksByOffset[Header.StartTotalOffset] = astLoop;

            if (Parent != null)
            {
                Parent.Body.ExceptWith(Body);
                Parent.Body.Add(astLoop);
            }
        }
    }

    private abstract class Selection : GroupingConstruct
    {
        public ASTBlock Header { get; init; } = null!;
        public ASTBlock Merge { get; init; } = null!;
    }

    private class JumpIfSelection : Selection
    {
        public override void Construct()
        {
            if (Header.Outbound.Count() != 2)
                throw new Exception("Something went wrong trying to construct a JumpIf");

            var lastInstruction = ((ASTRootOpInstruction)((ASTNormalBlock)Header).Instructions.Last()).RootInstruction;
            var jumpTarget = lastInstruction.Offset + lastInstruction.Args[0].Value;
            var thenBlock = Header.Outbound.SingleOrDefault(b => b.StartTotalOffset != jumpTarget);
            var elseBlock = Header.Outbound.SingleOrDefault(b => b.StartTotalOffset == jumpTarget);
            if (elseBlock != Merge)
                throw new Exception("Something went wrong trying to construct a JumpIf");

            var header = (ASTNormalBlock)Header;
            var astCondition = new ASTNormalBlock()
            {
                BlocksByOffset = header.BlocksByOffset,
                Instructions = new()
                {
                    new ASTReturn()
                    {
                        Value = Decompiler.ExpressionFromConditionOp(
                            lastInstruction.Args[1],
                            lastInstruction.Args[2],
                            (ScriptRootConditionOp)lastInstruction.Args[3].Value,
                            negateByInstruction: true
                        )
                    }
                }
            };
            astCondition.FixChildrenParents();
            Header.BlocksByOffset[-Header.StartTotalOffset] = astCondition;

            var astIfElse = new ASTIfElse()
            {
                BlocksByOffset = Header.BlocksByOffset,
                Prefix = header.Instructions.Any() ? header : null,
                Condition = astCondition,
                ThenOffset = thenBlock?.StartTotalOffset,
                ElseOffset = null,
                ContinueOffset = Merge == (Parent as Selection)?.Merge ? null : Merge.StartTotalOffset,
                StartOwnOffset = header.StartTotalOffset,
                EndOwnOffset = header.EndTotalOffset
            };
            //header.Instructions.RemoveLast(); // the JumpIf instruction
            Header.BlocksByOffset[header.StartTotalOffset] = astIfElse;
            foreach (var lastBlock in Merge.Inbound.Union(Body))
                lastBlock.ConstructProvidesControlFlow = true;
            Header.Parent = astIfElse;
            astCondition.Parent = astIfElse;
            foreach (var body in Body)
                body.Parent = astIfElse;

            if (Parent != null)
            {
                Parent.Body.ExceptWith(Body);
                Parent.Body.Remove(Header);
                Parent.Body.Add(astIfElse);
            }
        }
    }

    private class JumpIfCalcSelection : Selection
    {
        public override void Construct()
        {
            if (Header.Outbound.Count() > 2)
                throw new Exception("Something went wrong trying to construct a JumpIfCalc construct");

            var lastInstruction = ((ASTRootOpInstruction)((ASTNormalBlock)Header).Instructions.Last()).RootInstruction;
            var thenOffset = lastInstruction.Offset + lastInstruction.Args[0].Value;
            var elseOffset = lastInstruction.Offset + lastInstruction.Args[1].Value;
            
            var astIfElse = new ASTIfElse()
            {
                BlocksByOffset = Header.BlocksByOffset,
                Condition = Header,
                ThenOffset = thenOffset,
                ElseOffset = elseOffset == Merge.StartTotalOffset ? null : elseOffset,
                ContinueOffset = Merge == (Parent as Selection)?.Merge ? null : Merge.StartTotalOffset,
                StartOwnOffset = Header.StartTotalOffset,
                EndOwnOffset = Header.EndTotalOffset
            };
            //((ASTNormalBlock)Header).Instructions.RemoveLast(); // the JumpIfCalc instruction
            Header.BlocksByOffset[Header.StartTotalOffset] = astIfElse;

            foreach (var lastBlock in Merge.Inbound.Union(Body))
                lastBlock.ConstructProvidesControlFlow = true;
            Header.Parent = astIfElse;
            foreach (var body in Body)
                body.Parent = astIfElse;

            if (Parent != null)
            {
                Parent.Body.ExceptWith(Body);
                Parent.Body.Remove(Header);
                Parent.Body.Add(astIfElse);
            }
        }
    }

    private class SwitchSelection : Selection
    {
        public override void Construct()
        {
        }
    }

    private void ConstructLoops(IReadOnlyList<NaturalLoop> rootLoops)
    {
        var allLoops = rootLoops.SelectMany(l => l.AllChildren).OrderByDescending(l => l.Rank);
        foreach (var loop in allLoops.OfType<NaturalLoop>())
        {
            loop.Construct();
        }
    }
    
    private void ConstructSelections(List<Selection> rootSelections)
    {
        var allSelections = rootSelections.SelectMany(s => s.AllChildren).OrderByDescending(s => s.Rank);
        foreach (var selection in allSelections.OfType<Selection>())
        {
            selection.Construct();
        }
    }

    private void ConstructGotos()
    {
        return; // would not work for selections nor loops, rework entirely
        foreach (var block in blocksByOffset.Values.OfType<ASTNormalBlock>())
        {
            if (block.Instructions.LastOrDefault() is not ASTRootOpInstruction rootInstr ||
                block.ConstructProvidesControlFlow)
                continue;

            int target = block.EndTotalOffset;
            var removeLast = false;
            if (rootInstr.RootInstruction.Op == ScriptOp.Jump)
            {
                removeLast = true;
                target = rootInstr.RootInstruction.Offset + rootInstr.RootInstruction.Args[0].Value;
            }
            else if (SplittingOps.Contains(rootInstr.RootInstruction.Op))
                continue;
            if (target == block.EndTotalOffset)
                continue;

            ASTInstruction addInstr;
            if (target == script.Length)
                addInstr = new ASTReturn();
            else
            {
                var targetBlock = blocksByOffset[target] // TODO: you kno
                    ?? throw new Exception($"This should not have happened, goto instruction has invalid target");
                targetBlock.IsLabeled = true;
                addInstr = new ASTGoto() { Target = target };
            }

            addInstr.Parent = block;
            if (removeLast)
            {
                block.Instructions.RemoveLast();
                addInstr.StartOwnOffset = rootInstr.StartTotalOffset;
                addInstr.EndOwnOffset = rootInstr.EndTotalOffset;
            }
            block.Instructions.Add(addInstr);
        }
    }

    private void ConstructContinues()
    {
        // does not work for selections nor loops
        foreach (var block in blocksByOffset.Values)
        {
            
            if (block.ContinueBlock != null ||
                block.ConstructProvidesControlFlow ||
                !block.CanFallthrough ||
                block.EndTotalOffset == astExit.StartTotalOffset)
                continue;
            block.ContinueOffset = block.EndTotalOffset;
        }
    }
}
