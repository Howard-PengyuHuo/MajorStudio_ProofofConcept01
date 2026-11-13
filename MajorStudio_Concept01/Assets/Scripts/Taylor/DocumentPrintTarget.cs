using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CarryOn.Taylor
{
    // Share one target for the whole frame: TMP submit and Update must agree,
    // even when printing changes focus or disables an input during that frame.
    internal static class DocumentPrintTarget
    {
        private static readonly HashSet<Transform> documents = new();
        private static readonly List<RaycastResult> hits = new();
        private static int resolvedFrame = -1;
        private static Transform target;
        public static bool InputBlocked { get; set; }

        public static bool EnterPressed => Keyboard.current != null &&
            (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);

        public static void Register(Transform root)
        {
            if (root != null) documents.Add(root);
            resolvedFrame = -1;
        }

        public static void Unregister(Transform root)
        {
            if (root != null) documents.Remove(root);
        }

        private static Transform FindDocument(Transform child)
        {
            for (var t = child; t != null; t = t.parent)
                if (documents.Contains(t) && t.gameObject.activeInHierarchy) return t;
            return null;
        }

        public static bool IsTarget(Transform root)
        {
            if (InputBlocked) return false;
            if (root == null) return false;
            if (resolvedFrame != Time.frameCount)
            {
                resolvedFrame = Time.frameCount;
                target = null;
                var events = EventSystem.current;
                if (events != null)
                {
                    if (Mouse.current != null)
                    {
                        hits.Clear();
                        events.RaycastAll(new PointerEventData(events) { position = Mouse.current.position.ReadValue() }, hits);
                        // Only the foremost graphic counts; never print through another UI.
                        if (hits.Count > 0) target = FindDocument(hits[0].gameObject.transform);
                    }
                    if (target == null && events.currentSelectedGameObject != null)
                        target = FindDocument(events.currentSelectedGameObject.transform);
                }
            }
            return target == root;
        }
    }
}
