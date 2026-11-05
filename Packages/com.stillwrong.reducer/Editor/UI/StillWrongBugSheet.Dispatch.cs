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
    // Sending the picked bug to the agent.
    public sealed partial class StillWrongBugSheet
    {
        // One Send at a time. A second click while one runs would reduce the reduced copy.
        [NonSerialized]
        private bool _dispatchBusy;
        // Reduction edits scenes, so a Send pressed in Play Mode leaves Play Mode first and then sends.
        [SerializeField]
        private bool _sendAfterPlay;
        [InitializeOnLoadMethod]
        private static void HookSendAfterPlay()
        {
            EditorApplication.playModeStateChanged -= SendAfterPlay;
            EditorApplication.playModeStateChanged += SendAfterPlay;
        }
        private static void SendAfterPlay(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }
            StillWrongBugSheet[] sheets = Resources.FindObjectsOfTypeAll<StillWrongBugSheet>();
            for (int i = 0; i < sheets.Length; i++)
            {
                StillWrongBugSheet sheet = sheets[i];
                if (sheet != null && sheet._sendAfterPlay)
                {
                    sheet._sendAfterPlay = false;
                    EditorApplication.delayCall += sheet.DispatchBugNow;
                }
            }
        }
        private void DispatchBugNow()
        {
            if (_dispatchBusy)
            {
                return;
            }
            if (EditorApplication.isPlaying)
            {
                _sendAfterPlay = true;
                _progressLine = "Leaving Play Mode, then sending.";
                Repaint();
                EditorApplication.isPlaying = false;
                return;
            }
            _dispatchBusy = true;
            _checkLine = string.Empty;
            string chunk;
            string file;
            double clock;
            List<string> paths;
            GatherDispatchArgs(out chunk, out file, out clock, out paths);
            string sessionDir = _sessionDir;
            string candidatePath = _confirmedPath;
            string sentence = _sentence;
            _progressLine = "Checking this frame.";
            Repaint();
            FrozenSessionSheet.BeginAuthorSpec(sentence, candidatePath, 6, authored =>
            {
                string progress = string.Empty;
                string failure;
                try
                {
                    failure = RollingRecorderControl.RunBugDispatch(sessionDir, chunk, file, clock, candidatePath, paths, sentence, authored, out progress);
                }
                catch (Exception ex)
                {
                    failure = "failed: " + ex.GetType().Name + ": " + ex.Message;
                    progress = string.Empty;
                }
                _dispatchBusy = false;
                if (!string.IsNullOrEmpty(failure))
                {
                    _progressLine = failure;
                }
                else
                {
                    _progressLine = progress;
                }
                Repaint();
            });
        }
        private void DispatchBugUnverifiedNow()
        {
            string chunk;
            string file;
            double clock;
            List<string> paths;
            GatherDispatchArgs(out chunk, out file, out clock, out paths);
            _progressLine = "Sending this frame.";
            Repaint();
            RollingRecorderControl.BeginBugDispatchUnverified(_sessionDir, chunk, file, clock, _confirmedPath, paths, _sentence, (failure, progress) =>
            {
                if (!string.IsNullOrEmpty(failure))
                {
                    _progressLine = failure;
                }
                else
                {
                    _progressLine = progress;
                }
                Repaint();
            });
        }
        private void GatherDispatchArgs(out string chunk, out string file, out double clock, out List<string> paths)
        {
            chunk = string.Empty;
            file = string.Empty;
            clock = 0.0;
            if (_selectedThumb >= 0 && _selectedThumb < _thumbLabels.Count)
            {
                string label = _thumbLabels[_selectedThumb];
                int space = label.IndexOf(" ");
                if (space > 0)
                {
                    chunk = label.Substring(0, space);
                    file = label.Substring(space + 1) + ".jpg";
                }
            }
            if (_selectedThumb >= 0 && _selectedThumb < _thumbTimes.Count)
            {
                clock = _thumbTimes[_selectedThumb];
            }
            if (string.IsNullOrEmpty(_confirmedPath) && CurrentCandidate() != null && !string.IsNullOrEmpty(CurrentCandidate().ElementPath))
            {
                ConfirmCurrent();
            }
            paths = new List<string>();
            if (_candidates != null)
            {
                for (int i = 0; i < _candidates.Count; i++)
                {
                    if (_candidates[i] != null && !string.IsNullOrEmpty(_candidates[i].ElementPath))
                    {
                        paths.Add(_candidates[i].ElementPath);
                    }
                }
            }
        }
        public string DispatchSelectedBug(string sentence)
        {
            if (!string.IsNullOrEmpty(sentence))
            {
                _sentence = sentence;
            }
            DispatchBugNow();
            return _progressLine;
        }
        public string SheetProgress()
        {
            return _progressLine;
        }
    }
}
