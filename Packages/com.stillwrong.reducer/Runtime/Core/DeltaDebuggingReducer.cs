using System;
using System.Collections.Generic;
using System.Linq;

namespace StillWrong.Core
{
    public interface ITrialExecutor
    {
        OracleVerdict ExecuteTrial(ISet<string> candidateRemovals);
    }

    public sealed class ReductionTrialRecord
    {
        public int TrialIndex { get; }
        public int Granularity { get; }
        public IReadOnlyList<string> TestedRemovalIds { get; }
        public OracleVerdict Verdict { get; }
        public bool WasRetained { get; }
        public int RemainingCandidateCount { get; }
        public string Note { get; }

        public ReductionTrialRecord(
            int trialIndex,
            int granularity,
            IEnumerable<string> testedRemovalIds,
            OracleVerdict verdict,
            bool wasRetained,
            int remainingCandidateCount,
            string note = "")
        {
            TrialIndex = trialIndex;
            Granularity = granularity;
            TestedRemovalIds = testedRemovalIds?.ToList() ?? new List<string>();
            Verdict = verdict;
            WasRetained = wasRetained;
            RemainingCandidateCount = remainingCandidateCount;
            Note = note ?? string.Empty;
        }

        public override string ToString() =>
            $"Trial #{TrialIndex} (n={Granularity}): tested {TestedRemovalIds.Count} items -> {Verdict} (retained={WasRetained}, remaining={RemainingCandidateCount})";
    }

    public sealed class ReductionResult
    {
        public int InitialCandidateCount { get; }
        public int FinalCandidateCount { get; }
        public int RemovedCount => InitialCandidateCount - FinalCandidateCount;
        public IReadOnlyList<string> KeptNodeIds { get; }
        public IReadOnlyList<string> RemovedNodeIds { get; }
        public IReadOnlyList<ReductionTrialRecord> AuditLog { get; }
        public bool IsMinimized { get; }

        public ReductionResult(
            int initialCandidateCount,
            int finalCandidateCount,
            IEnumerable<string> keptNodeIds,
            IEnumerable<string> removedNodeIds,
            IEnumerable<ReductionTrialRecord> auditLog,
            bool isMinimized)
        {
            InitialCandidateCount = initialCandidateCount;
            FinalCandidateCount = finalCandidateCount;
            KeptNodeIds = keptNodeIds?.ToList() ?? new List<string>();
            RemovedNodeIds = removedNodeIds?.ToList() ?? new List<string>();
            AuditLog = auditLog?.ToList() ?? new List<ReductionTrialRecord>();
            IsMinimized = isMinimized;
        }
    }

    public sealed class ReductionNode
    {
        public string Id { get; }
        public string? ParentId { get; }
        public IReadOnlyList<string> Children { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public double PriorityScore { get; }

        public ReductionNode(
            string id,
            string? parentId = null,
            IEnumerable<string>? children = null,
            IEnumerable<string>? dependencies = null,
            double priorityScore = 0.0)
        {
            Id = id ?? string.Empty;
            ParentId = parentId;
            Children = children?.ToList() ?? new List<string>();
            Dependencies = dependencies?.ToList() ?? new List<string>();
            PriorityScore = priorityScore;
        }
    }

    public sealed class ReductionContext
    {
        public IReadOnlyList<ReductionNode> AllNodes { get; }
        public string TargetNodeId { get; }
        public string CameraNodeId { get; }
        public string EventSystemNodeId { get; }
        public IReadOnlyList<string> ExplicitlyProtectedIds { get; }
        public IReadOnlyDictionary<string, double>? PriorityScores { get; }

        public ReductionContext(
            IEnumerable<ReductionNode> allNodes,
            string targetNodeId,
            string cameraNodeId = "",
            string eventSystemNodeId = "",
            IEnumerable<string>? explicitlyProtectedIds = null,
            IReadOnlyDictionary<string, double>? priorityScores = null)
        {
            AllNodes = allNodes?.ToList() ?? new List<ReductionNode>();
            TargetNodeId = targetNodeId ?? string.Empty;
            CameraNodeId = cameraNodeId ?? string.Empty;
            EventSystemNodeId = eventSystemNodeId ?? string.Empty;
            ExplicitlyProtectedIds = explicitlyProtectedIds?.ToList() ?? new List<string>();
            PriorityScores = priorityScores;
        }
    }

    public static class DeltaDebuggingReducer
    {
        public static HashSet<string> ComputeProtectedClosure(ReductionContext context)
        {
            var protectedSet = new HashSet<string>(StringComparer.Ordinal);
            var nodeDict = context.AllNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

            void ProtectWithAncestors(string id)
            {
                if (string.IsNullOrEmpty(id)) return;
                string? curr = id;
                while (!string.IsNullOrEmpty(curr) && nodeDict.TryGetValue(curr, out var node))
                {
                    if (protectedSet.Add(node.Id))
                    {
                        foreach (var dep in node.Dependencies)
                        {
                            ProtectWithAncestors(dep);
                        }
                    }
                    curr = node.ParentId;
                }
            }

            ProtectWithAncestors(context.TargetNodeId);
            ProtectWithAncestors(context.CameraNodeId);
            ProtectWithAncestors(context.EventSystemNodeId);

            foreach (var explicitId in context.ExplicitlyProtectedIds)
            {
                ProtectWithAncestors(explicitId);
            }

            return protectedSet;
        }

        public static ReductionResult Reduce(ReductionContext context, ITrialExecutor executor)
        {
            if (context == null || executor == null)
            {
                throw new ArgumentNullException(context == null ? nameof(context) : nameof(executor));
            }
            var session = new ReductionSession(context);
            while (true)
            {
                ISet<string> trial = session.NextTrial();
                if (trial == null)
                {
                    break;
                }
                session.Report(executor.ExecuteTrial(trial));
            }
            return session.FinalResult;
        }

        internal static bool IsSubtreeEntirelyUnprotected(
            string rootId,
            Dictionary<string, ReductionNode> nodeDict,
            HashSet<string> protectedClosure)
        {
            if (protectedClosure.Contains(rootId)) return false;
            if (!nodeDict.TryGetValue(rootId, out var node)) return true;

            foreach (var childId in node.Children)
            {
                if (!IsSubtreeEntirelyUnprotected(childId, nodeDict, protectedClosure))
                {
                    return false;
                }
            }

            return true;
        }

        internal static List<string> CollectSubtree(string rootId, Dictionary<string, ReductionNode> nodeDict)
        {
            var result = new List<string>();
            var stack = new Stack<string>();
            stack.Push(rootId);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                result.Add(cur);
                if (nodeDict.TryGetValue(cur, out var node))
                {
                    foreach (var child in node.Children)
                    {
                        stack.Push(child);
                    }
                }
            }

            return result;
        }

        internal static List<List<string>> SplitIntoSubsets(List<string> items, int n)
        {
            int safeN = Math.Max(1, Math.Min(n, items.Count));
            var subsets = new List<List<string>>(safeN);
            for (int i = 0; i < safeN; i++)
            {
                subsets.Add(new List<string>());
            }

            for (int i = 0; i < items.Count; i++)
            {
                subsets[i % safeN].Add(items[i]);
            }

            return subsets.Where(s => s.Count > 0).ToList();
        }
    }
}
