using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Multiplayer
{
    /// <summary>Local pointer feedback only. Never submits or modifies a network selection.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Button), typeof(Image))]
    public sealed class CharacterHoverFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image characterImage;
        [SerializeField] private Material hoverMaterial;
        [SerializeField] private GameObject descriptionImage;
        [SerializeField] private CharacterHoverFeedback otherCharacter;
        private Material originalMaterial;

        public bool IsHovered { get; private set; }

        private void Awake()
        {
            if (characterImage != null) originalMaterial = characterImage.material;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!isActiveAndEnabled) return;
            // The explicit peer reference is local to this prefab, not shared across network instances.
            if (otherCharacter != null) otherCharacter.ClearHover();
            IsHovered = true;
            // The shared material is immutable; only this Image's material reference changes.
            // A shader tint can highlight black silhouettes where ordinary multiplicative tint cannot.
            if (characterImage != null && hoverMaterial != null) characterImage.material = hoverMaterial;
            if (descriptionImage != null) descriptionImage.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData) => ClearHover();
        private void OnEnable() => ClearHover();
        private void OnDisable() => ClearHover();
        private void OnApplicationFocus(bool focused) { if (!focused) ClearHover(); }

        public void ClearHover()
        {
            IsHovered = false;
            if (characterImage != null) characterImage.material = originalMaterial;
            if (descriptionImage != null) descriptionImage.SetActive(false);
        }
    }
}
