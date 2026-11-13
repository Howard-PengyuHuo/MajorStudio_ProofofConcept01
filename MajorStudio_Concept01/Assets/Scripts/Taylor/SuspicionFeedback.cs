using UnityEngine;

namespace CarryOn.Taylor
{
    // Presentation only: the authority still applies the existing reputation penalty.
    public sealed class SuspicionFeedback : MonoBehaviour
    {
        [SerializeField] private InterviewController interview;
        [SerializeField] private GameObject[] visuals;
        [SerializeField, Min(0.01f)] private float visibleSeconds = 1.5f;
        [SerializeField] private AudioSource suspicionAudio;
        [SerializeField] private AudioClip ehmmm;
        [SerializeField] private AudioClip writing;
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
            // Each actual suspicion increase refreshes the icon and plays one reaction.
            hideAt = Time.unscaledTime + visibleSeconds;
            SetVisible(true);
            var clip = Random.value < .5f ? ehmmm : writing;
            if (suspicionAudio != null && clip != null)
            {
                suspicionAudio.Stop();
                suspicionAudio.PlayOneShot(clip);
            }
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
