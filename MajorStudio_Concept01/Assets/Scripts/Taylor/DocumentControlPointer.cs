using UnityEngine;
using UnityEngine.EventSystems;

namespace CarryOn.Taylor
{
    // Inputs keep their own text selection; wheel and focus also affect their paper.
    public sealed class DocumentControlPointer : MonoBehaviour, IPointerDownHandler, IScrollHandler
    {
        private DraggableDocument document;
        private void Awake() => document = GetComponentInParent<DraggableDocument>();
        public void OnPointerDown(PointerEventData data) => document?.OnPointerDown(data);
        public void OnScroll(PointerEventData data) => document?.OnScroll(data);
    }
}
