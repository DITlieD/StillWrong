#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;

namespace StillWrong.Core
{
    public sealed class SceneObjectIdentity
    {
        public string HierarchyPath { get; }
        public string PrefabPath { get; }
        public string AssetGuid { get; }
        public string LocalFileId { get; }
        public IReadOnlyList<string> Components { get; }

        public SceneObjectIdentity(
            string hierarchyPath,
            string prefabPath = "",
            string assetGuid = "",
            string localFileId = "",
            IEnumerable<string>? components = null)
        {
            HierarchyPath = hierarchyPath ?? string.Empty;
            PrefabPath = prefabPath ?? string.Empty;
            AssetGuid = assetGuid ?? string.Empty;
            LocalFileId = localFileId ?? string.Empty;
            Components = components != null ? components.ToList() : (IReadOnlyList<string>)Array.Empty<string>();
        }

        public override string ToString() => $"SceneObject({HierarchyPath})";
    }

    public enum IdentityResolutionStatus
    {
        Success,
        NotFound,
        Ambiguous
    }

    public sealed class IdentityResolutionResult
    {
        public IdentityResolutionStatus Status { get; }
        public bool IsSuccess => Status == IdentityResolutionStatus.Success;
        public SceneObjectIdentity? ResolvedTarget { get; }
        public int ResolvedComponentIndex { get; }
        public string? FailureReason { get; }
        public IReadOnlyList<SceneObjectIdentity> MatchedCandidates { get; }

        private IdentityResolutionResult(
            IdentityResolutionStatus status,
            SceneObjectIdentity? target,
            int componentIndex,
            string? failureReason,
            IEnumerable<SceneObjectIdentity>? matchedCandidates)
        {
            Status = status;
            ResolvedTarget = target;
            ResolvedComponentIndex = componentIndex;
            FailureReason = failureReason;
            MatchedCandidates = matchedCandidates != null ? matchedCandidates.ToList() : (IReadOnlyList<SceneObjectIdentity>)Array.Empty<SceneObjectIdentity>();
        }

        public static IdentityResolutionResult Success(SceneObjectIdentity target, int componentIndex = 0)
        {
            return new IdentityResolutionResult(
                IdentityResolutionStatus.Success,
                target,
                componentIndex,
                null,
                new[] { target });
        }

        public static IdentityResolutionResult NotFound(string reason)
        {
            return new IdentityResolutionResult(
                IdentityResolutionStatus.NotFound,
                null,
                -1,
                reason,
                null);
        }

        public static IdentityResolutionResult Ambiguous(string reason, IEnumerable<SceneObjectIdentity> candidates)
        {
            return new IdentityResolutionResult(
                IdentityResolutionStatus.Ambiguous,
                null,
                -1,
                reason,
                candidates);
        }
    }

    public static class IdentityResolver
    {
        public static IdentityResolutionResult Resolve(
            SessionQualifiedId query,
            IEnumerable<SceneObjectIdentity> sceneObjects)
        {
            if (query == null)
            {
                return IdentityResolutionResult.NotFound("Query id is null");
            }

            var pool = sceneObjects?.ToList() ?? new List<SceneObjectIdentity>();
            if (pool.Count == 0)
            {
                return IdentityResolutionResult.NotFound("Scene candidate pool is empty");
            }

            if (!string.IsNullOrEmpty(query.AssetGuid) && !string.IsNullOrEmpty(query.LocalFileId))
            {
                var guidMatches = pool.FindAll(c =>
                    string.Equals(c.AssetGuid, query.AssetGuid, StringComparison.Ordinal) &&
                    string.Equals(c.LocalFileId, query.LocalFileId, StringComparison.Ordinal));

                if (guidMatches.Count == 1)
                {
                    return VerifyComponent(query, guidMatches[0]);
                }

                if (guidMatches.Count > 1)
                {
                    var filtered = FilterByPath(guidMatches, query.HierarchyPath, query.PrefabPath);
                    if (filtered.Count == 1)
                    {
                        return VerifyComponent(query, filtered[0]);
                    }
                    return IdentityResolutionResult.Ambiguous(
                        $"Multiple candidates match AssetGuid '{query.AssetGuid}' and LocalFileId '{query.LocalFileId}'",
                        guidMatches);
                }
            }

            if (!string.IsNullOrEmpty(query.HierarchyPath))
            {
                var exactPathMatches = pool.FindAll(c =>
                    string.Equals(c.HierarchyPath, query.HierarchyPath, StringComparison.Ordinal));

                if (exactPathMatches.Count == 1)
                {
                    return VerifyComponent(query, exactPathMatches[0]);
                }

                if (exactPathMatches.Count > 1)
                {
                    return IdentityResolutionResult.Ambiguous(
                        $"Multiple candidates share exact HierarchyPath '{query.HierarchyPath}'",
                        exactPathMatches);
                }

                var cloneMatches = pool.FindAll(c => MatchesClonedPath(c.HierarchyPath, query.HierarchyPath));
                if (cloneMatches.Count == 1)
                {
                    return VerifyComponent(query, cloneMatches[0]);
                }

                if (cloneMatches.Count > 1)
                {
                    return IdentityResolutionResult.Ambiguous(
                        $"Ambiguous cloned match for HierarchyPath '{query.HierarchyPath}'",
                        cloneMatches);
                }
            }

            if (!string.IsNullOrEmpty(query.PrefabPath))
            {
                var prefabMatches = pool.FindAll(c =>
                    string.Equals(c.PrefabPath, query.PrefabPath, StringComparison.Ordinal));

                if (prefabMatches.Count == 1)
                {
                    return VerifyComponent(query, prefabMatches[0]);
                }

                if (prefabMatches.Count > 1)
                {
                    var pathFiltered = FilterByPath(prefabMatches, query.HierarchyPath, null);
                    if (pathFiltered.Count == 1)
                    {
                        return VerifyComponent(query, pathFiltered[0]);
                    }

                    return IdentityResolutionResult.Ambiguous(
                        $"Multiple candidates match PrefabPath '{query.PrefabPath}'",
                        prefabMatches);
                }
            }

            return IdentityResolutionResult.NotFound(
                $"No matching scene object found for '{query.HierarchyPath}' ({query.AssetGuid})");
        }

        private static List<SceneObjectIdentity> FilterByPath(
            List<SceneObjectIdentity> list,
            string? hierarchyPath,
            string? prefabPath)
        {
            var filtered = list;
            if (!string.IsNullOrEmpty(hierarchyPath))
            {
                var byExact = filtered.FindAll(c =>
                    string.Equals(c.HierarchyPath, hierarchyPath, StringComparison.Ordinal));
                if (byExact.Count > 0) return byExact;

                var byClone = filtered.FindAll(c =>
                    MatchesClonedPath(c.HierarchyPath, hierarchyPath));
                if (byClone.Count > 0) return byClone;
            }

            if (!string.IsNullOrEmpty(prefabPath))
            {
                var byPrefab = filtered.FindAll(c =>
                    string.Equals(c.PrefabPath, prefabPath, StringComparison.Ordinal));
                if (byPrefab.Count > 0) return byPrefab;
            }

            return filtered;
        }

        private static bool MatchesClonedPath(string candidatePath, string queryPath)
        {
            if (string.Equals(candidatePath, queryPath, StringComparison.Ordinal))
            {
                return true;
            }

            string cleanCandidate = StripCloneSuffixes(candidatePath);
            string cleanQuery = StripCloneSuffixes(queryPath);

            if (string.Equals(cleanCandidate, cleanQuery, StringComparison.Ordinal))
            {
                return true;
            }

            if (cleanCandidate.EndsWith("/" + cleanQuery, StringComparison.Ordinal))
            {
                return true;
            }

            var querySegments = cleanQuery.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var candidateSegments = cleanCandidate.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (querySegments.Length > 0 && candidateSegments.Length >= querySegments.Length)
            {
                int offset = candidateSegments.Length - querySegments.Length;
                bool match = true;
                for (int i = 0; i < querySegments.Length; i++)
                {
                    if (!string.Equals(candidateSegments[offset + i], querySegments[i], StringComparison.Ordinal))
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }

            return false;
        }

        private static string StripCloneSuffixes(string path)
        {
            return path
                .Replace("(Clone)", string.Empty)
                .Replace(" ", string.Empty);
        }

        private static IdentityResolutionResult VerifyComponent(
            SessionQualifiedId query,
            SceneObjectIdentity candidate)
        {
            if (string.IsNullOrEmpty(query.ComponentType))
            {
                return IdentityResolutionResult.Success(candidate, 0);
            }

            int matchingIndex = -1;
            int currentMatchCount = 0;

            for (int i = 0; i < candidate.Components.Count; i++)
            {
                if (string.Equals(candidate.Components[i], query.ComponentType, StringComparison.OrdinalIgnoreCase))
                {
                    if (currentMatchCount == query.ComponentIndex)
                    {
                        matchingIndex = i;
                        break;
                    }
                    currentMatchCount++;
                }
            }

            if (matchingIndex >= 0)
            {
                return IdentityResolutionResult.Success(candidate, matchingIndex);
            }

            if (currentMatchCount > 0)
            {
                return IdentityResolutionResult.NotFound(
                    $"Target contains component '{query.ComponentType}' but index {query.ComponentIndex} is out of bounds (found {currentMatchCount})");
            }

            return IdentityResolutionResult.NotFound(
                $"Target object '{candidate.HierarchyPath}' does not contain required component '{query.ComponentType}'");
        }
    }
}
