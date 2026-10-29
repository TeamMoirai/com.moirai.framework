using UnityEngine;

namespace Moirai.Atropos.Diagnostics.Unity
{
    public sealed class DebuggerOverlayHost : MonoBehaviour
    {
        private GameObject _overlay;

        public bool IsVisible => _overlay != null;

        public void SetVisible(bool visible)
        {
            if (visible)
            {
                if (_overlay != null) return;
                _overlay = new GameObject("Moirai.DebuggerOverlay")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                return;
            }
            DestroyOverlay();
        }

        private void OnDestroy()
        {
            DestroyOverlay();
        }

        private void DestroyOverlay()
        {
            if (_overlay == null) return;
            if (Application.isPlaying) Destroy(_overlay);
            else DestroyImmediate(_overlay);
            _overlay = null;
        }
    }
}
