using UnityEngine;

namespace CarryOn.Taylor
{
    // Presentation only: the authority still applies the existing reputation penalty.
    public sealed class SuspicionFeedback : MonoBehaviour
    {
        [SerializeField] private InterviewController interview;
        [SerializeField] private GameObject[] visuals;
        [SerializeField, Min(0.01f)] private float visibleSeconds = 1.5f;
        private float hideAt = -1;

        private void OnEnable()
        {
            Hide();
            if (interview != null) interview.ReputationDropped += Show;
        }

        private void OnDisable()
        {
            if (interview != null) interview.ReputationDropped -= Show;
            Hide();
        }

        private void Show()
        {
            // Repeated shakes extend visibility until 1.5 seconds after the latest shake.
            hideAt = Time.unscaledTime + visibleSeconds;
            SetVisible(true);
        }

        private void Update()
        {
            if (hideAt >= 0 && Time.unscaledTime >= hideAt) Hide();
        }

        private void Hide()
        {
            hideAt = -1;
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (visuals == null) return;
            foreach (var visual in visuals) if (visual != null) visual.SetActive(visible);
        }
    }
}
