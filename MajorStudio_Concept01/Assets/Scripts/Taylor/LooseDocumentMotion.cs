using UnityEngine;
using UnityEngine.EventSystems;

namespace CarryOn.Taylor
{
    [RequireComponent(typeof(DraggableDocument))]
    public sealed class LooseDocumentMotion : MonoBehaviour, IPointerDownHandler
    {
        [SerializeField, Min(0)] private float slideSpeed = 420;
        [SerializeField, Min(0)] private float spinSpeed = 110;
        private RectTransform rect, bounds;
        private DraggableDocument drag;
        private Vector2 velocity;
        private float angularVelocity, remaining, nextKick;
        private readonly Vector3[] corners = new Vector3[4];

        private void Awake()
        {
            rect = (RectTransform)transform;
            bounds = rect.parent as RectTransform;
            drag = GetComponent<DraggableDocument>();
        }

        public void Shake(float seconds)
        {
            if (!isActiveAndEnabled) return;
            remaining = Mathf.Max(.1f, seconds);
            Kick();
        }

        private void Kick()
        {
            velocity = Random.insideUnitCircle.normalized * Random.Range(.6f, 1f) * slideSpeed;
            angularVelocity = Random.Range(-spinSpeed, spinSpeed);
            nextKick = Random.Range(.18f, .3f);
        }

        private void Update()
        {
            if (remaining <= 0 && velocity.sqrMagnitude < 1 && Mathf.Abs(angularVelocity) < 1) return;
            if (drag.IsDragging) { StopMotion(); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            remaining -= dt;
            nextKick -= dt;
            if (remaining > 0 && nextKick <= 0) Kick();
            rect.anchoredPosition += velocity * dt;
            rect.Rotate(0, 0, angularVelocity * dt);
            KeepOnDesk();
            float damping = Mathf.Exp(-(remaining > 0 ? 1.5f : 9f) * dt);
            velocity *= damping;
            angularVelocity *= damping;
        }

        private void KeepOnDesk()
        {
            if (bounds == null) return;
            rect.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in corners)
            {
                Vector2 p = bounds.InverseTransformPoint(corner);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            var box = bounds.rect;
            Vector2 correction = new Vector2(
                Correction(min.x, max.x, box.xMin, box.xMax),
                Correction(min.y, max.y, box.yMin, box.yMax));
            rect.anchoredPosition += correction;
            if (correction.x != 0) velocity.x *= -.5f;
            if (correction.y != 0) velocity.y *= -.5f;
        }

        private static float Correction(float min, float max, float low, float high) =>
            max - min > high - low ? (low + high - min - max) * .5f :
            min < low ? low - min : max > high ? high - max : 0;

        private void StopMotion() { remaining = 0; velocity = Vector2.zero; angularVelocity = 0; }
        public void OnPointerDown(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) StopMotion();
        }
        private void OnDisable() => StopMotion();
    }
}
