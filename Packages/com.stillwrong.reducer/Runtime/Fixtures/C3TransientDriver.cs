using UnityEngine;

namespace StillWrong.Fixtures
{
    public sealed class C3TransientDriver : MonoBehaviour
    {
        private CanvasGroup _badge;
        private int _frames;

        private void OnEnable()
        {
            _frames = 0;
            _badge = GetComponent<CanvasGroup>();
        }

        private void Update()
        {
            _frames++;
            if (_badge == null)
            {
                return;
            }
            if (_frames < 4)
            {
                _badge.alpha = 1f;
            }
            else if (_frames < 8)
            {
                _badge.alpha = 0f;
            }
            else
            {
                _badge.alpha = 0.85f;
            }
        }
    }
}

