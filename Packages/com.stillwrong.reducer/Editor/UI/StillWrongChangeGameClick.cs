using System.Collections.Generic;
using StillWrong.Oracle;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
namespace StillWrong.Editor.UI
{
    public static class StillWrongChangeGameClick
    {
        private static bool _gameArmed;
        public static void Arm()
        {
            _gameArmed = true;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }
        public static void Disarm()
        {
            _gameArmed = false;
            EditorApplication.update -= Poll;
        }
        private static void Poll()
        {
            if (!_gameArmed)
            {
                return;
            }
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }
            string path = ResolveGameViewClick();
            Disarm();
            if (!string.IsNullOrEmpty(path))
            {
                StillWrongChangePopup.OpenForPath(path);
            }
            StillWrongChangePopup.FocusPopup();
        }
        public static string ResolveGameViewClick()
        {
            return ResolveAt(Input.mousePosition);
        }
        public static string SimulateGameViewClick(Vector2 screenPoint)
        {
            string path = ResolveAt(screenPoint);
            Disarm();
            if (!string.IsNullOrEmpty(path))
            {
                StillWrongChangePopup.OpenForPath(path);
            }
            StillWrongChangePopup.FocusPopup();
            return path;
        }
        private static string ResolveAt(Vector2 screenPoint)
        {
            try
            {
                if (EventSystem.current == null)
                {
                    return string.Empty;
                }
                PointerEventData data = new PointerEventData(EventSystem.current);
                data.position = screenPoint;
                List<RaycastResult> hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(data, hits);
                if (hits.Count == 0 || hits[0].gameObject == null)
                {
                    return string.Empty;
                }
                StillWrongGroundingRequest ground = new StillWrongGroundingRequest();
                ground.PositivePoints.Add(screenPoint);
                StillWrongGroundingResult result = StillWrongGrounding.ActiveService.Ground(ground);
                if (result != null && result.Candidates != null && result.Candidates.Count > 0 && !string.IsNullOrEmpty(result.Candidates[0].ElementPath))
                {
                    return result.Candidates[0].ElementPath;
                }
                return UnitySymptomOracleRunner.GetHierarchyPath(hits[0].gameObject);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
