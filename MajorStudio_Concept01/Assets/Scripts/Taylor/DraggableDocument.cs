using UnityEngine;
using UnityEngine.EventSystems;

namespace CarryOn.Taylor
{
    [RequireComponent(typeof(RectTransform))]
    public class DraggableDocument : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform rect;
        private RectTransform area;
        private Vector2 startPointer, startPosition;
        private bool dragging;
        private int pointerId;

        private void Awake() => rect = (RectTransform)transform;

        public void OnPointerDown(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) transform.SetAsLastSibling();
        }

        public void OnBeginDrag(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left) return;
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
        }

        public void OnEndDrag(PointerEventData data)
        {
            if (data.pointerId == pointerId) dragging = false;
        }

        private void OnDisable() => dragging = false;
    }
}
