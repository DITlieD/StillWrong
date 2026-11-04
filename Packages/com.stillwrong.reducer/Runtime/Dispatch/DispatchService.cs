using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using StillWrong.Oracle;
using UnityEngine;
namespace StillWrong.Dispatch
{
    [Serializable]
    public sealed class DispatchTargetRef
    {
        public string Path = string.Empty;
        public string Name = string.Empty;
    }
    [Serializable]
    public sealed class DispatchPacket
    {
        public string Mode = string.Empty;
        public string SessionDir = string.Empty;
        public string FrameChunk = string.Empty;
        public string FrameFile = string.Empty;
        public double FrameClockSeconds;
        public string ScenePath = string.Empty;
        public string SourceScenePath = string.Empty;
        public DispatchTargetRef Target = new DispatchTargetRef();
        public List<string> CandidatePaths = new List<string>();
        public string Sentence = string.Empty;
        public string FailureVerdict = string.Empty;
        public List<string> FailureReasons = new List<string>();
        public List<string> KeptNodeIds = new List<string>();
        public List<string> RemovedNodeIds = new List<string>();
        public string FreshProcessVerdict = string.Empty;
        public string BundlePath = string.Empty;
        public string PreferredAgent = string.Empty;
        public string BranchPrefix = string.Empty;
        public string TargetBlocks = string.Empty;
    }
    [Serializable]
    public sealed class DispatchReceipt
    {
        public bool Sent;
        public bool Saved;
        public string Route = string.Empty;
        public string Branch = string.Empty;
        public string PacketFile = string.Empty;
        public string WorktreePath = string.Empty;
        public string Reason = string.Empty;
    }
    public static class DispatchService
    {
        public static string PacketRoot()
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "StillWrong", "dispatch");
        }
        public static DispatchReceipt SendPacket(DispatchPacket packet, string preferredAgent, string branchPrefix)
        {
            DispatchReceipt receipt = new DispatchReceipt();
            receipt.Route = RouteFor(preferredAgent);
            try
            {
                string root = PacketRoot();
                Directory.CreateDirectory(root);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                string file = Path.Combine(root, "packet-" + stamp + ".json");
                packet.PreferredAgent = preferredAgent;
                packet.BranchPrefix = branchPrefix;
                File.WriteAllText(file, JsonUtility.ToJson(packet, true));
                receipt.PacketFile = file;
            }
            catch (Exception ex)
            {
                receipt.Reason = "packet-write-failed: " + ex.GetType().Name;
                return receipt;
            }
            if (receipt.Route == "packet-export")
            {
                receipt.Saved = true;
                return receipt;
            }
            string branch = BranchFor(packet, branchPrefix);
            string created = TryCreateBranch(branch);
            if (!string.IsNullOrEmpty(created))
            {
                receipt.Reason = created;
                return receipt;
            }
            if (!BranchExists(branch))
            {
                receipt.Reason = "branch-verify-failed: " + branch;
                return receipt;
            }
            receipt.Branch = branch;
            if (receipt.Route == "muse-handoff")
            {
                string worktreeFailure = AttachMuseWorktree(receipt, branch, packet);
                if (!string.IsNullOrEmpty(worktreeFailure))
                {
                    receipt.Sent = false;
                    receipt.Saved = false;
                    receipt.Reason = worktreeFailure;
                    return receipt;
                }
            }
            receipt.Sent = true;
            return receipt;
        }
        private static string AttachMuseWorktree(DispatchReceipt receipt, string branch, DispatchPacket packet)
        {
            try
            {
                string stamp = Path.GetFileNameWithoutExtension(receipt.PacketFile);
                if (stamp.StartsWith("packet-"))
                {
                    stamp = stamp.Substring("packet-".Length);
                }
                string parent = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))), "stillwrong-worktrees");
                Directory.CreateDirectory(parent);
                string worktree = Path.Combine(parent, stamp);
                if (Directory.Exists(worktree))
                {
                    return "worktree-add-failed: already exists " + worktree;
                }
                string output;
                string error;
                int code = RunGit(ProjectRoot(), "worktree add \"" + worktree + "\" \"" + branch + "\"", out output, out error);
                if (code != 0)
                {
                    string why = string.IsNullOrWhiteSpace(error) ? output : error;
                    return "worktree-add-failed: " + why.Trim();
                }
                string copied = Path.Combine(worktree, Path.GetFileName(receipt.PacketFile));
                File.Copy(receipt.PacketFile, copied, false);
                receipt.WorktreePath = worktree;
                CopyReducedScene(packet, worktree);
                return string.Empty;
            }
            catch (Exception ex)
            {
                return "worktree-add-failed: " + ex.GetType().Name + " " + ex.Message;
            }
        }
        private static void CopyReducedScene(DispatchPacket packet, string worktree)
        {
            if (packet == null || string.IsNullOrEmpty(packet.ScenePath))
            {
                return;
            }
            string rel = packet.ScenePath.Replace("\\", "/");
            string source = Path.Combine(ProjectRoot(), rel);
            if (!File.Exists(source))
            {
                return;
            }
            string dest = Path.Combine(worktree, rel);
            string destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }
            File.Copy(source, dest, true);
            string metaSource = source + ".meta";
            if (File.Exists(metaSource))
            {
                File.Copy(metaSource, dest + ".meta", true);
            }
        }
        private static string RouteFor(string preferredAgent)
        {
            string agent = (preferredAgent ?? string.Empty).Trim().ToLowerInvariant();
            if (agent == "muse")
            {
                return "muse-handoff";
            }
            if (agent == "codex")
            {
                if (Type.GetType("StillWrong.Codex.CodexPacketSink, StillWrong.Codex") != null)
                {
                    return "codex-plugin";
                }
                return "packet-export";
            }
            return "packet-export";
        }
        private static string BranchFor(DispatchPacket packet, string branchPrefix)
        {
            string prefix = string.IsNullOrEmpty(branchPrefix) ? "stillwrong/" : branchPrefix;
            string name = packet != null && packet.Target != null && !string.IsNullOrEmpty(packet.Target.Name) ? packet.Target.Name : "untargeted";
            string clean = string.Empty;
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if ((ch >= 48 && ch <= 57) || (ch >= 65 && ch <= 90) || (ch >= 97 && ch <= 122) || ch == 45 || ch == 95)
                {
                    clean += ch;
                }
            }
            if (string.IsNullOrEmpty(clean))
            {
                clean = "untargeted";
            }
            string mode = packet != null && !string.IsNullOrEmpty(packet.Mode) ? packet.Mode.ToLowerInvariant() : "bug";
            if (mode != "change")
            {
                mode = "bug";
            }
            return prefix + mode + "/" + clean + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        }
        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
        public static string TryCreateBranch(string branch)
        {
            try
            {
                string root = ProjectRoot();
                if (!Directory.Exists(Path.Combine(root, ".git")))
                {
                    return "no-git-repo: packet kept, branch not created";
                }
                string list;
                string listError;
                int listCode = RunGit(root, "branch --list " + branch, out list, out listError);
                if (listCode == 0 && !string.IsNullOrWhiteSpace(list))
                {
                    return string.Empty;
                }
                string output;
                string error;
                int code = RunGit(root, "branch " + branch, out output, out error);
                if (code != 0)
                {
                    return "branch-create-failed: " + error;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                return "branch-create-failed: " + ex.GetType().Name;
            }
        }
        public static bool BranchExists(string branch)
        {
            try
            {
                string root = ProjectRoot();
                if (!Directory.Exists(Path.Combine(root, ".git")))
                {
                    return false;
                }
                string list;
                string listError;
                int code = RunGit(root, "branch --list " + branch, out list, out listError);
                return code == 0 && !string.IsNullOrWhiteSpace(list);
            }
            catch
            {
                return false;
            }
        }
        private static int RunGit(string workdir, string args, out string output, out string error)
        {
            output = string.Empty;
            error = string.Empty;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "git";
                info.Arguments = args;
                info.WorkingDirectory = workdir;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                Process proc = Process.Start(info);
                if (proc == null)
                {
                    error = "process-not-started";
                    return 1;
                }
                output = proc.StandardOutput.ReadToEnd();
                error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(30000);
                return proc.ExitCode;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name;
                return 1;
            }
        }
        public static OracleResult CheckFailureLive(OracleSpec spec)
        {
            UnityProbeSource source = new UnityProbeSource(spec);
            EvaluationContext context = new EvaluationContext();
            context.ConsoleErrorCount = 0;
            return OracleEvaluator.Evaluate(spec, source, context);
        }
        public static string FindSceneForPath(string hierarchyPath, out string scenePath)
        {
            scenePath = string.Empty;
            if (string.IsNullOrEmpty(hierarchyPath))
            {
                return "empty-target";
            }
            GameObject go = UnitySymptomOracleRunner.FindGameObjectByPath(hierarchyPath);
            if (go == null)
            {
                return "target-not-found";
            }
            try
            {
                scenePath = go.scene.path;
                if (string.IsNullOrEmpty(scenePath))
                {
                    return "scene-unsaved";
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                return "scene-lookup-failed: " + ex.GetType().Name;
            }
        }
    }
}
