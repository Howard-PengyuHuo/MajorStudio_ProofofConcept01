using UnityEngine;
using UnityEngine.EventSystems;

namespace CarryOn.Taylor
{
    [RequireComponent(typeof(RectTransform))]
    public class DraggableDocument : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        [SerializeField] private bool enableZoom;
        [SerializeField, Min(.1f)] private float minimumZoom = .5f;
        [SerializeField, Min(.1f)] private float maximumZoom = 2f;
        [SerializeField, Range(.01f, .5f)] private float zoomSpeed = .12f;
        [SerializeField] private bool resetOnEnable;
        [SerializeField] private RectTransform interactionBounds;
        private bool PointerAllowed(PointerEventData data) => interactionBounds == null ||
            RectTransformUtility.RectangleContainsScreenPoint(interactionBounds, data.position, data.enterEventCamera);

        public void ClampToBounds()
        {
            if (interactionBounds == null || rect == null) return;
            var size = Vector2.Scale(rect.rect.size, rect.localScale);
            var position = (Vector2)interactionBounds.InverseTransformPoint(rect.position);
            var bounds = interactionBounds.rect;
            position.x = Mathf.Clamp(position.x, bounds.xMin + size.x * rect.pivot.x, bounds.xMax - size.x * (1 - rect.pivot.x));
            position.y = Mathf.Clamp(position.y, bounds.yMin + size.y * rect.pivot.y, bounds.yMax - size.y * (1 - rect.pivot.y));
            rect.position = interactionBounds.TransformPoint(position);
        }
        private RectTransform rect;
        private RectTransform area;
        private Vector2 startPointer, startPosition;
        private bool dragging;
        public bool IsDragging => dragging;
        private int pointerId;
        private Vector3 originalScale;
        private Vector2 originalPosition;
        private float zoom = 1;

        private void Awake()
        {
            rect = (RectTransform)transform;
            originalScale = rect.localScale;
            originalPosition = rect.anchoredPosition;
        }

        private void OnEnable()
        {
            if (!resetOnEnable) return;
            rect.localScale = originalScale;
            rect.anchoredPosition = originalPosition;
            zoom = 1;
        }

        public void OnScroll(PointerEventData data)
        {
            if (!PointerAllowed(data) || !enableZoom || Mathf.Approximately(data.scrollDelta.y, 0)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position,
                data.enterEventCamera, out var localPoint)) return;
            float nextZoom = Mathf.Clamp(zoom * Mathf.Exp(Mathf.Clamp(data.scrollDelta.y, -3, 3) * zoomSpeed),
                Mathf.Max(.1f, minimumZoom), Mathf.Max(minimumZoom, maximumZoom));
            if (interactionBounds != null)
                nextZoom = Mathf.Min(nextZoom, interactionBounds.rect.width / (rect.rect.width * originalScale.x),
                    interactionBounds.rect.height / (rect.rect.height * originalScale.y));
            if (Mathf.Approximately(nextZoom, zoom)) return;
            // Keep the point beneath the mouse stationary while the whole document scales.
            Vector3 before = rect.TransformPoint(localPoint);
            zoom = nextZoom;
            rect.localScale = originalScale * zoom;
            rect.position += before - rect.TransformPoint(localPoint);
            ClampToBounds();
            transform.SetAsLastSibling();
            if (dragging && area != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(area, data.position,
                    data.enterEventCamera, out startPointer);
                startPosition = rect.anchoredPosition;
            }
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (PointerAllowed(data) && data.button == PointerEventData.InputButton.Left)
            {
                transform.SetAsLastSibling();
                DocumentHandCursor.Grab(this);
            }
        }

        public void OnBeginDrag(PointerEventData data)
        {
            if (!PointerAllowed(data) || data.button != PointerEventData.InputButton.Left) return;
            area = rect.parent as RectTransform;
            if (area == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                area, data.pressPosition, data.pressEventCamera, out startPointer)) return;
            transform.SetAsLastSibling();
            startPosition = rect.anchoredPosition;
            pointerId = data.pointerId;
            dragging = true;
        }

        public void OnDrag(PointerEventData data)
        {
            if (!dragging || data.pointerId != pointerId) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                area, data.position, data.pressEventCamera, out var position))
                rect.anchoredPosition = startPosition + position - startPointer;
            ClampToBounds();
        }

        public void OnEndDrag(PointerEventData data)
        {
            if (data.pointerId == pointerId) dragging = false;
        }

        private void OnDisable() => dragging = false;
    }
}
