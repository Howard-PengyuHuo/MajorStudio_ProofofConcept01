using TMPro;
using UnityEngine;

namespace CarryOn.Taylor
{
    [DefaultExecutionOrder(1000)]
    public sealed class HackerQuestionSubtitle : MonoBehaviour
    {
        [SerializeField] private TMP_Text questionText;
        [SerializeField] private CanvasGroup group;
        [SerializeField, Min(100)] private float maximumWidth = 1000;
        [SerializeField] private Vector2 padding = new Vector2(28, 18);
        [SerializeField, Min(0)] private float bottomOffset = 38;
        [SerializeField, Min(0)] private float fadeSeconds = .15f;
        private string previous;
        private float parentWidth;

        private void OnEnable()
        {
            previous = null;
            if (group != null) group.alpha = 0;
        }

        private void LateUpdate()
        {
            if (questionText == null || group == null) return;
            var rect = (RectTransform)transform;
            var parent = (RectTransform)rect.parent;
            if (previous != questionText.text || !Mathf.Approximately(parentWidth, parent.rect.width))
            {
                previous = questionText.text;
                parentWidth = parent.rect.width;
                float available = Mathf.Max(100, Mathf.Min(maximumWidth, parentWidth - 64) - padding.x * 2);
                float width = Mathf.Min(available, questionText.GetPreferredValues(previous, Mathf.Infinity, Mathf.Infinity).x);
                var preferred = questionText.GetPreferredValues(previous, Mathf.Max(1, width), Mathf.Infinity);
                rect.sizeDelta = new Vector2(width + padding.x * 2, preferred.y + padding.y * 2);
                rect.anchoredPosition = new Vector2(0, bottomOffset);
                questionText.rectTransform.offsetMin = padding;
                questionText.rectTransform.offsetMax = -padding;
            }
            float target = string.IsNullOrWhiteSpace(questionText.text) ? 0 : 1;
            group.alpha = fadeSeconds <= 0 ? target : Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / fadeSeconds);
        }
    }
}
