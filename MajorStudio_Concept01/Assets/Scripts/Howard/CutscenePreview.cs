using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Multiplayer
{
    /// <summary>Offline visual preview. Never connects to a room or starts gameplay.</summary>
    public sealed class CutscenePreview : MonoBehaviour
    {
        [SerializeField] private CutsceneCarousel carousel;
        private int page, previous = -1, direction = 1;
        private double startedAt;

        private void Start()
        {
            carousel.InitializePreview();
            startedAt = Time.unscaledTimeAsDouble;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rightArrowKey.wasPressedThisFrame) GoToPage(page + 1);
                else if (keyboard.leftArrowKey.wasPressedThisFrame) GoToPage(page - 1);
                else if (keyboard.homeKey.wasPressedThisFrame) GoToPage(0);
            }
            carousel.RenderPreview(page, previous, direction, startedAt);
        }

        public void GoToPage(int target)
        {
            target = Mathf.Clamp(target, 0, carousel.PanelCount);
            if (target == page) return;
            previous = page;
            direction = target > page ? 1 : -1;
            page = target;
            startedAt = Time.unscaledTimeAsDouble;
        }
    }
}
