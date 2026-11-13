using UnityEngine;

namespace CarryOn.Taylor
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class OfficerWritingSfx : MonoBehaviour
    {
        [SerializeField] private InterviewController interview;
        [SerializeField] private AudioClip writing;
        private AudioSource source;
        private int observedCount;
        private void Awake() => source = GetComponent<AudioSource>();
        private void OnEnable() => observedCount = interview != null ? interview.AcceptedAnswerCount : 0;
        private void LateUpdate()
        {
            if (interview == null) return;
            int count = interview.AcceptedAnswerCount;
            bool accepted = count > observedCount;
            observedCount = count;
            if (accepted && writing != null && Random.value < .7f)
                source.PlayOneShot(writing);
        }
        private void OnDisable() { if (source != null) source.Stop(); }
    }
}
