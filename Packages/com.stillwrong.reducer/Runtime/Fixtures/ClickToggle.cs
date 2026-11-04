using UnityEngine;
using UnityEngine.UI;
namespace StillWrong.Fixtures
{
    public sealed class ClickToggle : MonoBehaviour
    {
        public GameObject Target;
        public int Count;
        private void Awake()
        {
            Button button = GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(OnClick);
            }
        }
        public void OnClick()
        {
            Count++;
            if (Target != null)
            {
                Target.SetActive(!Target.activeSelf);
            }
        }
    }
}
