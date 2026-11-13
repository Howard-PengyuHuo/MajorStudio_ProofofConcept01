#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Multiplayer.Editor
{
    public static class VoiceWaveformChecks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        [MenuItem("Tools/Voice Visualization/Run Presentation Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit Mode.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Taylor/Canvas.prefab");
            var views = prefab.GetComponentsInChildren<VoiceWaveformVisualizer>(true);
            Check(views.Length == 1, "Exactly one Hacker waveform is authored");
            var authored = views[0].transform;
            var panel = UnityEngine.Object.Instantiate(authored.gameObject);
            panel.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var view = panel.GetComponent<VoiceWaveformVisualizer>();
                var background = panel.GetComponent<Image>();
                var sprite = background.sprite;
                var material = background.material;
                var position = background.rectTransform.anchoredPosition;
                var size = background.rectTransform.sizeDelta;
                Call(view, "Awake"); Call(view, "OnEnable");
                var bars = Get<Image[]>(view, "bars");
                var region = Get<RectTransform>(view, "waveformRegion");
                Check(bars.Length == 32 && sprite != null && panel.GetComponent<VoiceIndicatorGlow>() != null,
                    "Pool, original artwork and lamp are wired");
                Call(view, "Present", true, false, 1f, .016f);
                Check(view.State == VoiceVisualizationState.Inactive && !region.gameObject.activeSelf && view.DisplayedEnergy == 0,
                    "Closed comms cannot show activity");
                Call(view, "Present", true, true, 0f, .016f);
                Check(view.State == VoiceVisualizationState.Silent && bars[0].rectTransform.sizeDelta.y == 2,
                    "Silent baseline at minimum height");
                for (int i = 0; i < 120; i++) Call(view, "Present", true, true, .03f, 1f / 60);
                float quiet = view.DisplayedEnergy;
                for (int i = 0; i < 120; i++) Call(view, "Present", true, true, .3f, 1f / 60);
                Check(view.State == VoiceVisualizationState.Speaking && view.DisplayedEnergy > quiet,
                    "Greater measured energy produces a stronger envelope");
                Check(bars[0].rectTransform.sizeDelta.y != bars[1].rectTransform.sizeDelta.y, "Deterministic per-bar variation");
                Call(view, "Present", true, true, 0f, 1f / 60);
                Check(view.DisplayedEnergy > 0 && bars[0].rectTransform.sizeDelta.y > 2, "Smooth release, not a snap");
                for (int i = 0; i < 240; i++) Call(view, "Present", true, true, 0f, 1f / 60);
                Check(view.DisplayedEnergy < .0001f && Mathf.Abs(bars[0].rectTransform.sizeDelta.y - 2) < .01f,
                    "Silence returns to idle without random peaks");
                float width = region.rect.width;
                background.rectTransform.sizeDelta = new Vector2(size.x * 2, size.y);
                Call(view, "Present", true, true, 0f, .016f);
                Check(Mathf.Abs(region.rect.width - width) < 1, "Waveform follows preserveAspect letterboxing when resized");
                background.rectTransform.sizeDelta = size;
                Set(view, "barCount", 24);
                Call(view, "Present", true, true, 0f, .016f);
                Check(bars[23].gameObject.activeSelf && !bars[24].gameObject.activeSelf, "Count uses existing pool");
                foreach (var bar in bars) Check(!bar.raycastTarget && bar.rectTransform.pivot.y == .5f,
                    "Centered non-blocking bars");
                Call(view, "Present", false, false, 1f, .016f);
                Check(view.State == VoiceVisualizationState.Disconnected && !region.gameObject.activeSelf && view.DisplayedEnergy == 0,
                    "Disconnect clears waveform");
                Call(view, "OnDisable"); Call(view, "OnEnable");
                Check(Get<object>(view, "participant") == null && view.DisplayedEnergy == 0, "Lifecycle reset");
                Check(background.sprite == sprite && background.material == material && background.rectTransform.anchoredPosition == position &&
                    background.rectTransform.sizeDelta == size, "Existing artwork, material and layout preserved");
                Debug.Log("VOICE WAVEFORM CHECKS PASSED: synthetic energy, silence/loudness, attack/release, pool, resize/aspect, disconnect, lifecycle, lamp and artwork. Live Vivox microphone transmission is not tested by this fixture.");
            }
            finally { UnityEngine.Object.DestroyImmediate(panel); }
        }
        private static void Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Fields).Invoke(obj, args);
        private static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Fields).GetValue(obj);
        private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Fields).SetValue(obj, value);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif
