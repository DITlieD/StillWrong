using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StillWrong.Editor.Recording;
using StillWrong.Recording;
namespace StillWrong.Editor.UI
{
    // Checking the agent's copy and putting its fix into the user's scene.
    public sealed partial class StillWrongBugSheet
    {
        private void PutTheFixInMyScene()
        {
            UnityEngine.SceneManagement.Scene open = EditorSceneManager.GetActiveScene();
            string openPath = open.path ?? string.Empty;
            if (openPath.IndexOf("StillWrongDispatch/FixedCopy", StringComparison.Ordinal) < 0)
            {
                _checkLine = "Open the copy first. Your scene was not changed.";
                return;
            }
            if (string.IsNullOrEmpty(_returnScene))
            {
                _checkLine = "No scene to update. Your scene was not changed.";
                return;
            }
            if (_returnScene.IndexOf("StillWrongDispatch", StringComparison.Ordinal) >= 0)
            {
                _checkLine = "The return scene is a dispatch copy. Your scene was not changed.";
                return;
            }
            string project = Directory.GetParent(Application.dataPath).FullName;
            string destAbs = SceneAbs(project, _returnScene);
            if (!File.Exists(destAbs))
            {
                _checkLine = "No scene to update. Your scene was not changed.";
                return;
            }
            string openAbs = SceneAbs(project, openPath);
            if (!File.Exists(openAbs))
            {
                _checkLine = "Open the copy first. Your scene was not changed.";
                return;
            }
            StillWrong.Dispatch.DispatchPacket packet = ReadCheckPacket();
            string target = packet != null && packet.Target != null ? packet.Target.Name : string.Empty;
            if (string.IsNullOrEmpty(target))
            {
                _checkLine = "Target name is missing or not unique. Your scene was not changed.";
                return;
            }
            string sourceText = File.ReadAllText(openAbs);
            string destText = File.ReadAllText(destAbs);
            if (CountExactName(sourceText, target) != 1 || CountExactName(destText, target) != 1)
            {
                _checkLine = "Target name is missing or not unique. Your scene was not changed.";
                return;
            }
            string updated;
            if (!TryApplyObject(sourceText, destText, target, out updated))
            {
                _checkLine = "The component set does not match. Your scene was not changed.";
                return;
            }
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string backupDir = Path.Combine(project, "Library", "StillWrong", "backups", stamp);
            Directory.CreateDirectory(backupDir);
            File.Copy(destAbs, Path.Combine(backupDir, Path.GetFileName(destAbs)), false);
            File.WriteAllText(destAbs, updated);
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(_returnScene, OpenSceneMode.Single);
            _checkLine = "The fix is in your scene.";
        }
        private static string SceneAbs(string project, string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return string.Empty;
            }
            if (scenePath.StartsWith("Assets/"))
            {
                return Path.Combine(project, scenePath);
            }
            return scenePath;
        }
        private static StillWrong.Dispatch.DispatchPacket ReadCheckPacket()
        {
            string worktree = RollingRecorderControl.FindCheckWorktree();
            if (string.IsNullOrEmpty(worktree) || !Directory.Exists(worktree))
            {
                return null;
            }
            string[] packets = Directory.GetFiles(worktree, "packet-*.json");
            if (packets == null || packets.Length == 0)
            {
                return null;
            }
            string packetFile = packets[0];
            for (int i = 1; i < packets.Length; i++)
            {
                if (string.CompareOrdinal(packets[i], packetFile) > 0)
                {
                    packetFile = packets[i];
                }
            }
            try
            {
                return JsonUtility.FromJson<StillWrong.Dispatch.DispatchPacket>(File.ReadAllText(packetFile));
            }
            catch
            {
                return null;
            }
        }
        private static int CountExactName(string text, string targetName)
        {
            string[] rows = SplitScene(text);
            string nameLine = "m_Name: " + targetName;
            int count = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Trim() == nameLine)
                {
                    count++;
                }
            }
            return count;
        }
        public static string CaptureTargetBlocks(string scenePath, string targetName)
        {
            string abs = scenePath ?? string.Empty;
            if (abs.StartsWith("Assets/"))
            {
                string project = Directory.GetParent(Application.dataPath).FullName;
                abs = Path.Combine(project, abs);
            }
            if (string.IsNullOrEmpty(targetName) || !File.Exists(abs))
            {
                return string.Empty;
            }
            string text = File.ReadAllText(abs);
            if (CountExactName(text, targetName) != 1)
            {
                return string.Empty;
            }
            string canon;
            if (!TryCanonicalBlocks(text, targetName, out canon))
            {
                return string.Empty;
            }
            return canon;
        }
        private void FinishBlockLine(string sceneFile, string targetName, StillWrong.Dispatch.DispatchPacket packet)
        {
            string nowBlocks;
            if (!TryCanonicalBlocks(File.ReadAllText(sceneFile), targetName, out nowBlocks))
            {
                nowBlocks = string.Empty;
            }
            string thenBlocks = packet != null ? packet.TargetBlocks ?? string.Empty : string.Empty;
            if (string.IsNullOrEmpty(thenBlocks) || string.IsNullOrEmpty(nowBlocks))
            {
                _checkLine = "Opened the copy. Your scene was not changed.";
                return;
            }
            if (nowBlocks == thenBlocks)
            {
                _checkLine = "Still wrong. This is the copy. Your scene was not changed.";
                return;
            }
            _checkLine = "Fixed. This is the copy. Your scene was not changed.";
        }
        private static bool TryCanonicalBlocks(string text, string targetName, out string canon)
        {
            canon = string.Empty;
            List<string[]> blocks;
            if (!TryObjectBlocks(text, targetName, out blocks))
            {
                return false;
            }
            List<string> parts = new List<string>();
            for (int i = 0; i < blocks.Count; i++)
            {
                parts.Add(BlockBody(blocks[i]));
            }
            string[] rows = SplitScene(text);
            List<string> matIds = EmbeddedMaterialIds(rows, blocks);
            for (int i = 0; i < matIds.Count; i++)
            {
                int start;
                int end;
                if (!TryDocSpan(rows, matIds[i], out start, out end))
                {
                    return false;
                }
                string[] slice = new string[end - start];
                for (int j = start; j < end; j++)
                {
                    slice[j - start] = rows[j];
                }
                parts.Add(BlockBody(slice));
            }
            canon = string.Join("\n--block--\n", parts);
            return canon.Length > 0;
        }
        private static List<string> EmbeddedMaterialIds(string[] rows, List<string[]> blocks)
        {
            List<string> ids = new List<string>();
            if (rows == null || blocks == null)
            {
                return ids;
            }
            for (int b = 0; b < blocks.Count; b++)
            {
                string[] block = blocks[b];
                bool inMats = false;
                for (int i = 0; i < block.Length; i++)
                {
                    string trimmed = block[i].Trim();
                    if (trimmed.StartsWith("m_Materials:"))
                    {
                        inMats = true;
                        continue;
                    }
                    if (!inMats)
                    {
                        continue;
                    }
                    if (!trimmed.StartsWith("-"))
                    {
                        inMats = false;
                        continue;
                    }
                    if (trimmed.IndexOf("guid:", StringComparison.Ordinal) >= 0)
                    {
                        continue;
                    }
                    string id = FileIdToken(trimmed);
                    if (string.IsNullOrEmpty(id) || id == "0")
                    {
                        continue;
                    }
                    if (!IsLocalMaterial(rows, id))
                    {
                        continue;
                    }
                    if (!ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }
            return ids;
        }
        private static string FileIdToken(string line)
        {
            int key = line.IndexOf("fileID:", StringComparison.Ordinal);
            if (key < 0)
            {
                return string.Empty;
            }
            string rest = line.Substring(key + 7).Trim();
            int n = 0;
            while (n < rest.Length && char.IsDigit(rest[n]))
            {
                n++;
            }
            if (n == 0)
            {
                return string.Empty;
            }
            return rest.Substring(0, n);
        }
        private static bool IsLocalMaterial(string[] rows, string fileId)
        {
            string header = "--- !u!21 &" + fileId;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Trim() == header)
                {
                    return true;
                }
            }
            return false;
        }
        private static string BlockBody(string[] rows)
        {
            List<string> kept = new List<string>();
            for (int i = 1; i < rows.Length; i++)
            {
                if (rows[i].TrimStart().StartsWith("m_GameObject:"))
                {
                    continue;
                }
                kept.Add(rows[i]);
            }
            return string.Join("\n", kept);
        }
        private static bool TryApplyObject(string sourceText, string destText, string targetName, out string updated)
        {
            updated = destText;
            List<string[]> sourceBlocks;
            List<string[]> destBlocks;
            if (!TryObjectBlocks(sourceText, targetName, out sourceBlocks) || !TryObjectBlocks(destText, targetName, out destBlocks))
            {
                return false;
            }
            if (sourceBlocks.Count != destBlocks.Count || sourceBlocks.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < sourceBlocks.Count; i++)
            {
                if (BlockSignature(sourceBlocks[i]) != BlockSignature(destBlocks[i]))
                {
                    return false;
                }
            }
            string destId;
            if (!TryGameObjectFileId(destText, targetName, out destId))
            {
                return false;
            }
            string[] destRows = SplitScene(destText);
            List<string> ids = ComponentIds(destRows, destId);
            if (ids.Count != sourceBlocks.Count)
            {
                return false;
            }
            int[] starts = new int[ids.Count];
            int[] ends = new int[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                if (!TryDocSpan(destRows, ids[i], out starts[i], out ends[i]))
                {
                    return false;
                }
            }
            string[] sourceRows = SplitScene(sourceText);
            List<string> sourceMats = EmbeddedMaterialIds(sourceRows, sourceBlocks);
            List<string> destMats = EmbeddedMaterialIds(destRows, destBlocks);
            if (sourceMats.Count != destMats.Count)
            {
                return false;
            }
            int[] matSrc = PairMaterials(sourceMats, destMats);
            if (matSrc == null)
            {
                return false;
            }
            int total = ids.Count + destMats.Count;
            int[] allStart = new int[total];
            int[] allEnd = new int[total];
            string[][] pieces = new string[total][];
            for (int i = 0; i < ids.Count; i++)
            {
                allStart[i] = starts[i];
                allEnd[i] = ends[i];
                pieces[i] = RebuildBlock(sourceBlocks[i], destBlocks[i]);
                if (pieces[i] == null)
                {
                    return false;
                }
            }
            for (int m = 0; m < destMats.Count; m++)
            {
                int destStart;
                int destEnd;
                int sourceStart;
                int sourceEnd;
                if (!TryDocSpan(destRows, destMats[m], out destStart, out destEnd) || !TryDocSpan(sourceRows, sourceMats[matSrc[m]], out sourceStart, out sourceEnd))
                {
                    return false;
                }
                string[] sourceSlice = new string[sourceEnd - sourceStart];
                for (int j = sourceStart; j < sourceEnd; j++)
                {
                    sourceSlice[j - sourceStart] = sourceRows[j];
                }
                string[] destSlice = new string[destEnd - destStart];
                for (int j = destStart; j < destEnd; j++)
                {
                    destSlice[j - destStart] = destRows[j];
                }
                int slot = ids.Count + m;
                allStart[slot] = destStart;
                allEnd[slot] = destEnd;
                pieces[slot] = RebuildMaterial(sourceSlice, destSlice);
                if (pieces[slot] == null)
                {
                    return false;
                }
            }
            int[] order = new int[total];
            for (int i = 0; i < total; i++)
            {
                order[i] = i;
            }
            for (int a = 0; a < order.Length; a++)
            {
                for (int b = a + 1; b < order.Length; b++)
                {
                    if (allStart[order[b]] < allStart[order[a]])
                    {
                        int swap = order[a];
                        order[a] = order[b];
                        order[b] = swap;
                    }
                }
            }
            for (int n = 1; n < order.Length; n++)
            {
                if (allStart[order[n]] < allEnd[order[n - 1]])
                {
                    return false;
                }
            }
            List<string> merged = new List<string>();
            int cursor = 0;
            for (int n = 0; n < order.Length; n++)
            {
                int i = order[n];
                for (int j = cursor; j < allStart[i]; j++)
                {
                    merged.Add(destRows[j]);
                }
                for (int j = 0; j < pieces[i].Length; j++)
                {
                    merged.Add(pieces[i][j]);
                }
                cursor = allEnd[i];
            }
            for (int j = cursor; j < destRows.Length; j++)
            {
                merged.Add(destRows[j]);
            }
            updated = JoinScene(destText, merged.ToArray());
            for (int i = 0; i < ids.Count; i++)
            {
                if (CountDocFileId(updated, ids[i]) != 1)
                {
                    updated = destText;
                    return false;
                }
            }
            for (int i = 0; i < destMats.Count; i++)
            {
                if (CountDocFileId(updated, destMats[i]) != 1)
                {
                    updated = destText;
                    return false;
                }
            }
            return true;
        }
        private static int[] PairMaterials(List<string> sourceIds, List<string> destIds)
        {
            int[] pair = new int[destIds.Count];
            bool[] used = new bool[sourceIds.Count];
            for (int d = 0; d < destIds.Count; d++)
            {
                pair[d] = -1;
                for (int s = 0; s < sourceIds.Count; s++)
                {
                    if (used[s] || sourceIds[s] != destIds[d])
                    {
                        continue;
                    }
                    pair[d] = s;
                    used[s] = true;
                    break;
                }
            }
            int cursor = 0;
            for (int d = 0; d < destIds.Count; d++)
            {
                if (pair[d] >= 0)
                {
                    continue;
                }
                while (cursor < used.Length && used[cursor])
                {
                    cursor++;
                }
                if (cursor >= used.Length)
                {
                    return null;
                }
                pair[d] = cursor;
                used[cursor] = true;
                cursor++;
            }
            return pair;
        }
        private static string[] RebuildMaterial(string[] source, string[] dest)
        {
            if (source == null || dest == null || source.Length == 0 || dest.Length == 0)
            {
                return null;
            }
            List<string> rows = new List<string>();
            rows.Add(dest[0]);
            for (int i = 1; i < source.Length; i++)
            {
                rows.Add(source[i]);
            }
            return rows.ToArray();
        }
        private static int CountDocFileId(string text, string fileId)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fileId))
            {
                return 0;
            }
            string[] rows = SplitScene(text);
            int count = 0;
            string tail = "&" + fileId;
            for (int i = 0; i < rows.Length; i++)
            {
                string trimmed = rows[i].Trim();
                if (!trimmed.StartsWith("--- !u!"))
                {
                    continue;
                }
                int amp = trimmed.LastIndexOf('&');
                if (amp >= 0 && trimmed.Substring(amp).Trim() == tail)
                {
                    count++;
                }
            }
            return count;
        }
        private static string[] RebuildBlock(string[] source, string[] dest)
        {
            string destGo = null;
            for (int i = 0; i < dest.Length; i++)
            {
                if (dest[i].TrimStart().StartsWith("m_GameObject:"))
                {
                    destGo = dest[i];
                    break;
                }
            }
            if (destGo == null)
            {
                return null;
            }
            bool saw = false;
            List<string> rows = new List<string>();
            rows.Add(dest[0]);
            for (int i = 1; i < source.Length; i++)
            {
                if (source[i].TrimStart().StartsWith("m_GameObject:"))
                {
                    rows.Add(destGo);
                    saw = true;
                }
                else
                {
                    rows.Add(source[i]);
                }
            }
            if (!saw)
            {
                return null;
            }
            return rows.ToArray();
        }
        private static string BlockSignature(string[] block)
        {
            string header = block[0].Trim();
            int bang = header.IndexOf("!u!");
            int amp = header.LastIndexOf('&');
            string typeId = string.Empty;
            if (bang >= 0 && amp > bang)
            {
                typeId = header.Substring(bang, amp - bang).Trim();
            }
            string className = block.Length > 1 ? block[1].Trim() : string.Empty;
            return typeId + " " + className;
        }
        private static bool TryObjectBlocks(string text, string targetName, out List<string[]> blocks)
        {
            blocks = new List<string[]>();
            string goId;
            if (!TryGameObjectFileId(text, targetName, out goId))
            {
                return false;
            }
            string[] rows = SplitScene(text);
            List<string> ids = ComponentIds(rows, goId);
            if (ids.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < ids.Count; i++)
            {
                int start;
                int end;
                if (!TryDocSpan(rows, ids[i], out start, out end))
                {
                    return false;
                }
                string[] slice = new string[end - start];
                for (int j = start; j < end; j++)
                {
                    slice[j - start] = rows[j];
                }
                blocks.Add(slice);
            }
            return true;
        }
        private static List<string> ComponentIds(string[] rows, string goId)
        {
            List<string> ids = new List<string>();
            string header = "--- !u!1 &" + goId;
            int doc = -1;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Trim() == header)
                {
                    doc = i;
                    break;
                }
            }
            if (doc < 0)
            {
                return ids;
            }
            for (int i = doc + 1; i < rows.Length; i++)
            {
                string trimmed = rows[i].Trim();
                if (trimmed.StartsWith("--- "))
                {
                    break;
                }
                if (!trimmed.StartsWith("- component:"))
                {
                    continue;
                }
                int key = trimmed.IndexOf("fileID:");
                if (key < 0)
                {
                    continue;
                }
                string rest = trimmed.Substring(key + 7).Trim().TrimEnd('}');
                ids.Add(rest.Trim());
            }
            return ids;
        }
        private static bool TryDocSpan(string[] rows, string fileId, out int start, out int end)
        {
            start = -1;
            end = -1;
            for (int i = 0; i < rows.Length; i++)
            {
                string trimmed = rows[i].Trim();
                if (!trimmed.StartsWith("--- !u!"))
                {
                    continue;
                }
                int amp = trimmed.LastIndexOf('&');
                if (amp < 0 || trimmed.Substring(amp + 1).Trim() != fileId)
                {
                    continue;
                }
                int stop = rows.Length;
                for (int j = i + 1; j < rows.Length; j++)
                {
                    if (rows[j].TrimStart().StartsWith("--- "))
                    {
                        stop = j;
                        break;
                    }
                }
                start = i;
                end = stop;
                return true;
            }
            return false;
        }

        private static bool TryApplyTransform(string sourceText, string destText, string targetName, out string updated, out string scaleLine)
        {
            updated = destText;
            scaleLine = string.Empty;
            string sourceId;
            string destId;
            if (!TryGameObjectFileId(sourceText, targetName, out sourceId) || !TryGameObjectFileId(destText, targetName, out destId))
            {
                return false;
            }
            string[] sourceRows = SplitScene(sourceText);
            string[] destRows = SplitScene(destText);
            int sourceStart;
            int sourceEnd;
            int destStart;
            int destEnd;
            if (!TryTransformSpan(sourceRows, sourceId, out sourceStart, out sourceEnd) || !TryTransformSpan(destRows, destId, out destStart, out destEnd))
            {
                return false;
            }
            string[] keys = new string[] { "m_LocalRotation:", "m_LocalPosition:", "m_LocalScale:", "m_LocalEulerAnglesHint:" };
            for (int k = 0; k < keys.Length; k++)
            {
                int from = FindField(sourceRows, sourceStart, sourceEnd, keys[k]);
                int to = FindField(destRows, destStart, destEnd, keys[k]);
                if (from < 0 || to < 0)
                {
                    return false;
                }
                string indent = destRows[to].Substring(0, destRows[to].Length - destRows[to].TrimStart().Length);
                destRows[to] = indent + sourceRows[from].Trim();
                if (keys[k] == "m_LocalScale:")
                {
                    scaleLine = destRows[to].Trim();
                }
            }
            updated = JoinScene(destText, destRows);
            return true;
        }
        private static bool TryGameObjectFileId(string text, string targetName, out string fileId)
        {
            fileId = string.Empty;
            string[] rows = SplitScene(text);
            string nameLine = "m_Name: " + targetName;
            int at = -1;
            int hits = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Trim() == nameLine)
                {
                    hits++;
                    at = i;
                }
            }
            if (hits != 1)
            {
                return false;
            }
            for (int i = at; i >= 0; i--)
            {
                string row = rows[i].Trim();
                if (row.StartsWith("--- !u!1 &"))
                {
                    fileId = row.Substring("--- !u!1 &".Length).Trim();
                    return fileId.Length > 0;
                }
                if (row.StartsWith("--- "))
                {
                    return false;
                }
            }
            return false;
        }
        private static bool TryTransformSpan(string[] rows, string goId, out int start, out int end)
        {
            start = -1;
            end = -1;
            string needle = "m_GameObject: {fileID: " + goId + "}";
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Trim() != needle)
                {
                    continue;
                }
                int doc = -1;
                for (int j = i; j >= 0; j--)
                {
                    if (rows[j].TrimStart().StartsWith("--- "))
                    {
                        if (rows[j].TrimStart().StartsWith("--- !u!4 "))
                        {
                            doc = j;
                        }
                        break;
                    }
                }
                if (doc < 0)
                {
                    continue;
                }
                int stop = rows.Length;
                for (int j = doc + 1; j < rows.Length; j++)
                {
                    if (rows[j].TrimStart().StartsWith("--- "))
                    {
                        stop = j;
                        break;
                    }
                }
                start = doc;
                end = stop;
                return true;
            }
            return false;
        }
        private static int FindField(string[] rows, int start, int end, string key)
        {
            int found = -1;
            for (int i = start; i < end; i++)
            {
                if (rows[i].TrimStart().StartsWith(key))
                {
                    if (found >= 0)
                    {
                        return -1;
                    }
                    found = i;
                }
            }
            return found;
        }
        private static string[] SplitScene(string text)
        {
            string norm = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            if (norm.EndsWith("\n"))
            {
                norm = norm.Substring(0, norm.Length - 1);
            }
            return norm.Split('\n');
        }
        private static string JoinScene(string original, string[] rows)
        {
            string joined = string.Join("\n", rows);
            bool trail = (original ?? string.Empty).Replace("\r\n", "\n").EndsWith("\n");
            if (trail)
            {
                joined += "\n";
            }
            if ((original ?? string.Empty).Contains("\r\n"))
            {
                joined = joined.Replace("\n", "\r\n");
            }
            return joined;
        }
        private void BackToMyScene()
        {
            if (string.IsNullOrEmpty(_returnScene))
            {
                return;
            }
            string abs = _returnScene;
            if (_returnScene.StartsWith("Assets/"))
            {
                abs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, _returnScene);
            }
            if (!File.Exists(abs))
            {
                _checkLine = "Scene to return to is missing. Your scene was not changed.";
                return;
            }
            EditorSceneManager.OpenScene(_returnScene, OpenSceneMode.Single);
        }
        private void CheckTheCopy()
        {
            string worktree = RollingRecorderControl.FindCheckWorktree();
            if (string.IsNullOrEmpty(worktree) || !Directory.Exists(worktree))
            {
                _checkLine = "No copy to check. Your scene was not changed.";
                return;
            }
            string[] packets = Directory.GetFiles(worktree, "packet-*.json");
            if (packets == null || packets.Length == 0)
            {
                _checkLine = "No copy to check. Your scene was not changed.";
                return;
            }
            string packetFile = packets[0];
            for (int i = 1; i < packets.Length; i++)
            {
                if (string.CompareOrdinal(packets[i], packetFile) > 0)
                {
                    packetFile = packets[i];
                }
            }
            StillWrong.Dispatch.DispatchPacket packet = null;
            try
            {
                packet = JsonUtility.FromJson<StillWrong.Dispatch.DispatchPacket>(File.ReadAllText(packetFile));
            }
            catch
            {
                packet = null;
            }
            if (packet == null || string.IsNullOrEmpty(packet.ScenePath))
            {
                _checkLine = "Copy not found. Your scene was not changed.";
                return;
            }
            string rel = packet.ScenePath.Replace("\\", "/");
            string source = Path.Combine(worktree, rel);
            if (!File.Exists(source))
            {
                _checkLine = "Copy not found. Your scene was not changed.";
                return;
            }
            string destDir = Path.Combine(Application.dataPath, "StillWrongDispatch", "FixedCopy");
            Directory.CreateDirectory(destDir);
            string fileName = Path.GetFileName(source);
            string dest = Path.Combine(destDir, fileName);
            File.Copy(source, dest, true);
            string meta = source + ".meta";
            if (File.Exists(meta))
            {
                File.Copy(meta, dest + ".meta", true);
                AssignFreshGuid(dest + ".meta");
            }
            AssetDatabase.Refresh();
            string assetPath = "Assets/StillWrongDispatch/FixedCopy/" + fileName;
            UnityEngine.SceneManagement.Scene open = EditorSceneManager.GetActiveScene();
            string openPath = open.path ?? string.Empty;
            string sourceScene = packet.SourceScenePath ?? string.Empty;
            sourceScene = sourceScene.Replace('\\', '/');
            if (!string.IsNullOrEmpty(sourceScene) && sourceScene.IndexOf("StillWrongDispatch", StringComparison.Ordinal) < 0)
            {
                _returnScene = sourceScene;
            }
            else if (string.IsNullOrEmpty(packet.SourceScenePath) && !string.IsNullOrEmpty(openPath) && openPath.IndexOf("StillWrongDispatch", StringComparison.Ordinal) < 0)
            {
                _returnScene = openPath;
            }
            EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Single);
            string target = packet.Target != null ? packet.Target.Name : string.Empty;
            if (!IsScalePacket(packet))
            {
                FinishBlockLine(dest, target, packet);
                return;
            }
            float x;
            float y;
            float z;
            if (!TryReadScale(dest, target, out x, out y, out z))
            {
                _checkLine = "Opened the copy. Your scene was not changed.";
                return;
            }
            float largest = Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z)));
            string numbers = x.ToString("0.###") + ", " + y.ToString("0.###") + ", " + z.ToString("0.###");
            if (largest > 1.2f)
            {
                _checkLine = "Still wrong. This is the copy. Your scene was not changed. " + numbers;
            }
            else
            {
                _checkLine = "Fixed. This is the copy. Your scene was not changed. " + numbers;
            }
        }
        private static void AssignFreshGuid(string metaPath)
        {
            if (string.IsNullOrEmpty(metaPath) || !File.Exists(metaPath))
            {
                return;
            }
            string body = File.ReadAllText(metaPath);
            string[] rows = body.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            for (int i = 0; i < rows.Length; i++)
            {
                string trimmed = rows[i].Trim();
                if (!trimmed.StartsWith("guid:"))
                {
                    continue;
                }
                string oldGuid = trimmed.Substring(5).Trim();
                string fresh = NewSceneGuid(oldGuid);
                int pad = rows[i].Length - rows[i].TrimStart().Length;
                rows[i] = rows[i].Substring(0, pad) + "guid: " + fresh;
                string joined = string.Join("\n", rows);
                if (body.Contains("\r\n"))
                {
                    joined = joined.Replace("\n", "\r\n");
                }
                File.WriteAllText(metaPath, joined);
                return;
            }
        }
        private static string NewSceneGuid(string oldGuid)
        {
            string fresh = Guid.NewGuid().ToString("N");
            int guard = 0;
            while (!string.IsNullOrEmpty(oldGuid) && fresh == oldGuid && guard < 4)
            {
                fresh = Guid.NewGuid().ToString("N");
                guard++;
            }
            return fresh;
        }
        private static bool IsScalePacket(StillWrong.Dispatch.DispatchPacket packet)
        {
            if (packet == null)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(packet.Sentence) && packet.Sentence.IndexOf("scale", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (!string.IsNullOrEmpty(packet.FreshProcessVerdict) && packet.FreshProcessVerdict.StartsWith("scale"))
            {
                return true;
            }
            if (packet.FailureReasons == null)
            {
                return false;
            }
            for (int i = 0; i < packet.FailureReasons.Count; i++)
            {
                string reason = packet.FailureReasons[i];
                if (!string.IsNullOrEmpty(reason) && reason.StartsWith("scale"))
                {
                    return true;
                }
            }
            return false;
        }
        private static bool TryReadScale(string sceneFile, string targetName, out float x, out float y, out float z)
        {
            x = 0f;
            y = 0f;
            z = 0f;
            if (string.IsNullOrEmpty(targetName) || !File.Exists(sceneFile))
            {
                return false;
            }
            string[] lines = File.ReadAllLines(sceneFile);
            string nameLine = "m_Name: " + targetName;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != nameLine)
                {
                    continue;
                }
                int end = Mathf.Min(i + 24, lines.Length);
                for (int j = i; j < end; j++)
                {
                    string row = lines[j].Trim();
                    if (!row.StartsWith("m_LocalScale:"))
                    {
                        continue;
                    }
                    return ParseScale(row, out x, out y, out z);
                }
            }
            return false;
        }
        private static bool ParseScale(string row, out float x, out float y, out float z)
        {
            x = 0f;
            y = 0f;
            z = 0f;
            int xs = row.IndexOf("x:");
            int ys = row.IndexOf("y:");
            int zs = row.IndexOf("z:");
            if (xs < 0 || ys < 0 || zs < 0)
            {
                return false;
            }
            string xv = row.Substring(xs + 2, ys - (xs + 2)).Trim().TrimEnd(","[0]);
            string yv = row.Substring(ys + 2, zs - (ys + 2)).Trim().TrimEnd(","[0]);
            int brace = row.IndexOf("}", zs);
            string zv = brace > zs ? row.Substring(zs + 2, brace - (zs + 2)).Trim() : row.Substring(zs + 2).Trim();
            return float.TryParse(xv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && float.TryParse(yv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) && float.TryParse(zv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
        }
    }
}
