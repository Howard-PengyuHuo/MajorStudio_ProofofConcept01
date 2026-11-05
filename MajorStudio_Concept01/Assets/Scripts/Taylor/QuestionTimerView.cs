using TMPro;
using UnityEngine;

namespace CarryOn.Taylor
{
    public sealed class QuestionTimerView : MonoBehaviour
    {
        [SerializeField] private InterviewController interview;
        [SerializeField] private TMP_Text timerText;
        private Color normalColor;

        private void Awake()
        {
            if (timerText != null) normalColor = timerText.color;
        }

        private void LateUpdate()
        {
            if (interview == null || timerText == null) return;
            timerText.text = interview.QuestionTimerRunning
                ? Mathf.CeilToInt(interview.QuestionSecondsRemaining) + "s" : "";
            timerText.color = interview.QuestionSecondsRemaining <= 5
                ? new Color(1f, .35f, .25f) : normalColor;
        }
    }
}
