using System;
using System.Collections.Generic;
using System.Linq;

namespace StillWrong.Core
{
    public sealed class ReductionSession
    {
        private readonly ReductionContext context;
        private readonly HashSet<string> protectedClosure;
        private readonly Dictionary<string, ReductionNode> nodeDict;
        private readonly List<string> candidatePool;
        private readonly HashSet<string> confirmedRemovals;
        private readonly List<ReductionTrialRecord> auditLog;
        private readonly int initialCandidateCount;
        private readonly List<string> pruneRoots;
        private int prunePos;
        private int trialIndex;
        private int granularity;
        private List<List<string>> currentSubsets;
        private int subsetPos;
        private bool complementPhase;
        private List<string> pendingChunk;
        private List<string> pendingSubset;
        private string pendingNote;
        private int pendingGranularity;
        private bool pendingIsSet;
        private bool finished;
        private ReductionResult finalResult;

        public ReductionSession(ReductionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            this.context = context;
            protectedClosure = DeltaDebuggingReducer.ComputeProtectedClosure(context);
            nodeDict = context.AllNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            candidatePool = context.AllNodes
                .Where(n => !protectedClosure.Contains(n.Id))
                .OrderByDescending(n => Score(n.Id))
                .Select(n => n.Id)
                .ToList();
            confirmedRemovals = new HashSet<string>(StringComparer.Ordinal);
            auditLog = new List<ReductionTrialRecord>();
            initialCandidateCount = candidatePool.Count;
            trialIndex = 1;
            pruneRoots = candidatePool
                .Where(id => nodeDict.TryGetValue(id, out var n) && n.Children.Count > 0)
                .Where(id => DeltaDebuggingReducer.IsSubtreeEntirelyUnprotected(id, nodeDict, protectedClosure))
                .OrderBy(Depth)
                .ThenByDescending(Score)
                .ToList();
            prunePos = 0;
            granularity = Math.Min(2, candidatePool.Count);
            currentSubsets = null;
            subsetPos = 0;
            complementPhase = false;
            pendingIsSet = false;
            finished = false;
            if (candidatePool.Count == 0)
            {
                Finish();
            }
        }

        public bool IsFinished
        {
            get { return finished; }
        }

        public IReadOnlyCollection<string> ConfirmedRemovals
        {
            get { return confirmedRemovals.ToList(); }
        }

        public IReadOnlyList<ReductionTrialRecord> AuditLog
        {
            get { return auditLog.ToList(); }
        }

        public ReductionResult FinalResult
        {
            get
            {
                if (!finished)
                {
                    throw new InvalidOperationException("Reduction session is not finished yet.");
                }
                return finalResult;
            }
        }

        public ISet<string> NextTrial()
        {
            if (finished)
            {
                return null;
            }
            if (pendingIsSet)
            {
                throw new InvalidOperationException("Report must be called for the outstanding trial before NextTrial.");
            }
            // A subtree whose root was already removed with a parent cannot change the result, so skip it.
            while (prunePos < pruneRoots.Count && confirmedRemovals.Contains(pruneRoots[prunePos]))
            {
                prunePos++;
            }
            if (prunePos < pruneRoots.Count)
            {
                List<string> chunk = DeltaDebuggingReducer.CollectSubtree(pruneRoots[prunePos], nodeDict);
                return StageTrial(chunk, 1, "SubtreePruning", null);
            }
            while (true)
            {
                if (candidatePool.Count == 0)
                {
                    Finish();
                    return null;
                }
                if (currentSubsets == null)
                {
                    currentSubsets = DeltaDebuggingReducer.SplitIntoSubsets(candidatePool, granularity);
                    subsetPos = 0;
                    complementPhase = false;
                }
                if (!complementPhase)
                {
                    if (subsetPos < currentSubsets.Count)
                    {
                        List<string> chunk = currentSubsets[subsetPos];
                        string note = "Subset_" + (subsetPos + 1) + "_of_" + currentSubsets.Count;
                        return StageTrial(chunk, granularity, note, null);
                    }
                    if (granularity > 2)
                    {
                        complementPhase = true;
                        subsetPos = 0;
                        continue;
                    }
                    if (!AdvanceGranularity())
                    {
                        Finish();
                        return null;
                    }
                    continue;
                }
                while (subsetPos < currentSubsets.Count)
                {
                    List<string> subset = currentSubsets[subsetPos];
                    List<string> complement = candidatePool.Where(id => !subset.Contains(id)).ToList();
                    if (complement.Count == 0)
                    {
                        subsetPos++;
                        continue;
                    }
                    string note = "Complement_" + (subsetPos + 1) + "_of_" + currentSubsets.Count;
                    return StageTrial(complement, granularity, note, subset);
                }
                if (!AdvanceGranularity())
                {
                    Finish();
                    return null;
                }
            }
        }

        public void Report(OracleVerdict verdict)
        {
            if (!pendingIsSet)
            {
                throw new InvalidOperationException("No outstanding trial to report.");
            }
            bool retained = verdict == OracleVerdict.StillWrong;
            if (pendingSubset != null)
            {
                int remaining = retained ? candidatePool.Count - pendingChunk.Count : candidatePool.Count;
                auditLog.Add(new ReductionTrialRecord(trialIndex++, pendingGranularity, pendingChunk, verdict, retained, remaining, pendingNote));
                if (retained)
                {
                    confirmedRemovals.UnionWith(pendingChunk);
                    candidatePool.Clear();
                    candidatePool.AddRange(pendingSubset);
                    granularity = RecomputeGranularity();
                    currentSubsets = null;
                    subsetPos = 0;
                    complementPhase = false;
                }
                else
                {
                    subsetPos++;
                }
            }
            else if (complementPhase)
            {
                throw new InvalidOperationException("Complement trial is missing its subset.");
            }
            else if (prunePos < pruneRoots.Count)
            {
                if (retained)
                {
                    confirmedRemovals.UnionWith(pendingChunk);
                    candidatePool.RemoveAll(pendingChunk.Contains);
                }
                auditLog.Add(new ReductionTrialRecord(trialIndex++, 1, pendingChunk, verdict, retained, candidatePool.Count, pendingNote));
                prunePos++;
            }
            else
            {
                int remaining = retained ? candidatePool.Count - pendingChunk.Count : candidatePool.Count;
                auditLog.Add(new ReductionTrialRecord(trialIndex++, pendingGranularity, pendingChunk, verdict, retained, remaining, pendingNote));
                if (retained)
                {
                    confirmedRemovals.UnionWith(pendingChunk);
                    candidatePool.RemoveAll(pendingChunk.Contains);
                    granularity = RecomputeGranularity();
                    currentSubsets = null;
                    subsetPos = 0;
                    complementPhase = false;
                }
                else
                {
                    subsetPos++;
                }
            }
            pendingIsSet = false;
            pendingChunk = null;
            pendingSubset = null;
            if (candidatePool.Count == 0)
            {
                Finish();
            }
        }

        private ISet<string> StageTrial(List<string> chunk, int trialGranularity, string note, List<string> subset)
        {
            var testSet = new HashSet<string>(confirmedRemovals, StringComparer.Ordinal);
            testSet.UnionWith(chunk);
            pendingChunk = chunk;
            pendingSubset = subset;
            pendingNote = note;
            pendingGranularity = trialGranularity;
            pendingIsSet = true;
            return testSet;
        }

        private bool AdvanceGranularity()
        {
            if (granularity >= candidatePool.Count)
            {
                return false;
            }
            granularity = Math.Min(granularity * 2, candidatePool.Count);
            currentSubsets = null;
            subsetPos = 0;
            complementPhase = false;
            return true;
        }

        private int RecomputeGranularity()
        {
            if (candidatePool.Count <= 1)
            {
                return 1;
            }
            return Math.Min(Math.Max(granularity - 1, 2), candidatePool.Count);
        }

        // Number of ancestors, so whole branches near the scene root are tried before their children.
        private int Depth(string id)
        {
            int depth = 0;
            string current = id;
            while (nodeDict.TryGetValue(current, out var node) && !string.IsNullOrEmpty(node.ParentId) && depth < 1000)
            {
                depth++;
                current = node.ParentId;
            }
            return depth;
        }

        private double Score(string id)
        {
            if (context.PriorityScores != null && context.PriorityScores.TryGetValue(id, out double s))
            {
                return s;
            }
            if (nodeDict.TryGetValue(id, out var node))
            {
                return node.PriorityScore;
            }
            return 0.0;
        }

        private void Finish()
        {
            if (finished)
            {
                return;
            }
            var allNodeIds = context.AllNodes.Select(n => n.Id).ToList();
            var keptNodes = allNodeIds.Where(id => !confirmedRemovals.Contains(id)).ToList();
            finalResult = new ReductionResult(
                initialCandidateCount,
                candidatePool.Count,
                keptNodes,
                confirmedRemovals.ToList(),
                auditLog,
                true);
            finished = true;
        }
    }
}
