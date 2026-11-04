using UnityEngine;
namespace StillWrong.Fixtures
{
    public sealed class AwakeSpawnProbe : MonoBehaviour
    {
        public const string ChildSuffix = "_AwakeChild";
        public static int AwakeCount;
        public static void ResetCounts()
        {
            AwakeCount = 0;
        }
        private void Awake()
        {
            AwakeCount++;
            GameObject child = new GameObject(gameObject.name + ChildSuffix);
            child.transform.SetParent(transform, false);
        }
    }
}
