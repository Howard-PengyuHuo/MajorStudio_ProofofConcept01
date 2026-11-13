using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using TMPro;

namespace CarryOn.Taylor
{
    public sealed class DocumentHandCursor : MonoBehaviour
    {
        private static DocumentHandCursor instance;
        private Texture2D openHand, closedHand, typingHand;
        private Texture2D appliedTexture;
        private GameObject selectedObject;
        private TMP_InputField tmpInput;
        private UnityEngine.UI.InputField legacyInput;
        private DraggableDocument heldDocument;
        private bool closed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (instance != null) return;
            var owner = new GameObject("Document Hand Cursor");
            DontDestroyOnLoad(owner);
            instance = owner.AddComponent<DocumentHandCursor>();
            instance.openHand = Resources.Load<Texture2D>("HandCursors/handOpen");
            instance.closedHand = Resources.Load<Texture2D>("HandCursors/handClosed");
            instance.typingHand = Resources.Load<Texture2D>("HandCursors/handType");
            instance.Apply(false);
        }

        public static void Grab(DraggableDocument document)
        {
            if (instance == null || !Application.isPlaying) return;
            instance.heldDocument = document;
            instance.Apply(true);
        }

        private void Update()
        {
            if (closed && (Mouse.current == null || !Mouse.current.leftButton.isPressed ||
                heldDocument == null || !heldDocument.isActiveAndEnabled)) Release();
            Apply(closed);
        }

        private bool IsEditingInput()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != selectedObject)
            {
                selectedObject = selected;
                tmpInput = selected != null ? selected.GetComponentInParent<TMP_InputField>() : null;
                legacyInput = selected != null ? selected.GetComponentInParent<UnityEngine.UI.InputField>() : null;
            }
            return (tmpInput != null && tmpInput.isActiveAndEnabled && tmpInput.interactable && !tmpInput.readOnly && tmpInput.isFocused)
                || (legacyInput != null && legacyInput.isActiveAndEnabled && legacyInput.interactable && !legacyInput.readOnly && legacyInput.isFocused);
        }

        private void Release()
        {
            heldDocument = null;
            Apply(false);
        }

        private void Apply(bool grabbing)
        {
            closed = grabbing;
            var texture = grabbing ? closedHand : IsEditingInput() && typingHand != null ? typingHand : openHand;
            if (texture == appliedTexture) return;
            appliedTexture = texture;
            // The center of the palm stays at the same click/drag point in both images.
            var hotspot = texture != null ? new Vector2(texture.width * .5f, texture.height * .5f) : Vector2.zero;
            Cursor.SetCursor(texture, hotspot, CursorMode.Auto);
        }

        private void OnApplicationFocus(bool focused) => Release();

        private void OnDestroy()
        {
            if (instance != this) return;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            instance = null;
        }
    }
}
