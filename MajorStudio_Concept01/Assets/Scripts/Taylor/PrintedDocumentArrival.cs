using System.Collections;
using UnityEngine;

namespace CarryOn.Taylor
{
    [DefaultExecutionOrder(100)]
    public sealed class PrintedDocumentArrival : MonoBehaviour
    {
        [SerializeField, Min(.1f)] private float duration = .8f;
        private RectTransform rect;
        private Vector2 destination;
        private DraggableDocument drag;
        private void Awake()
        {
            rect = (RectTransform)transform;
            destination = rect.anchoredPosition;
            drag = GetComponent<DraggableDocument>();
        }
        private void OnEnable() => StartCoroutine(Arrive());
        private IEnumerator Arrive()
        {
            if (drag != null) drag.enabled = false;
            var parent = (RectTransform)rect.parent;
            var start = new Vector2(parent.rect.xMax + rect.rect.width * rect.localScale.x, destination.y);
            rect.anchoredPosition = start;
            for (float time = 0; time < duration; time += Time.unscaledDeltaTime)
            {
                rect.anchoredPosition = Vector2.Lerp(start, destination, Mathf.SmoothStep(0, 1, time / duration));
                yield return null;
            }
            rect.anchoredPosition = destination;
            if (drag != null) { drag.enabled = true; drag.ClampToBounds(); }
        }
        private void OnDisable()
        {
            StopAllCoroutines();
            if (rect != null) rect.anchoredPosition = destination;
            if (drag != null) drag.enabled = true;
        }
    }
}
