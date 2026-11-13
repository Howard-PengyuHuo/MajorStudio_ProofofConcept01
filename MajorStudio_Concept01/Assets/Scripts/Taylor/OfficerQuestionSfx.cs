using UnityEngine;

namespace CarryOn.Taylor
{
    // Attached only to the traveler's interview panel, so the hacker never hears this locally.
    [RequireComponent(typeof(AudioSource))]
    public sealed class OfficerQuestionSfx : MonoBehaviour
    {
        [SerializeField] private InterviewController interview;
        [SerializeField] private AudioClip hmm;
        [SerializeField] private AudioClip clearThroat;
        private AudioSource source;
        private string lastQuestion;
        private int lastIndex = -1;

        private void Awake() => source = GetComponent<AudioSource>();
        private void OnEnable() { lastQuestion = null; lastIndex = -1; }

        private void LateUpdate()
        {
            if (interview == null) return;
            if (interview.ReviewingPurpose || interview.IsShowingIntroduction || interview.IsFinished || interview.IsExplainingDenial)
            {
                lastQuestion = null;
                return;
            }
            string question = interview.CurrentQuestion;
            if (string.IsNullOrWhiteSpace(question)) { lastQuestion = null; return; }
            if (question == lastQuestion && interview.QuestionIndex == lastIndex) return;
            lastQuestion = question;
            lastIndex = interview.QuestionIndex;
            var clip = Random.value < .5f ? hmm : clearThroat;
            if (clip == null) return;
            source.Stop();
            source.PlayOneShot(clip);
        }

        private void OnDisable() { if (source != null) source.Stop(); }
    }
}
