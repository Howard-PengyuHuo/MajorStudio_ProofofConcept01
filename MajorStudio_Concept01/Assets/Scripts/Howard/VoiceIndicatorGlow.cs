using UnityEngine;
using UnityEngine.UI;

namespace Game.Multiplayer
{
    [RequireComponent(typeof(Image))]
    public sealed class VoiceIndicatorGlow : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float fadeSeconds = .15f;
        [SerializeField, Tooltip("Editor only: hold T to preview the lamp when Vivox is not connected. Never enables the microphone.")]
        private bool previewInEditor = true;
        private static readonly int LightAmount = Shader.PropertyToID("_LightAmount");
        private Image image;
        private Material original, runtime;
        private VoiceChatManager voice;
        private float amount, retryAt;

        private void Awake()
        {
            image = GetComponent<Image>();
            original = image.material;
            runtime = new Material(original) { name = "Voice Indicator (Runtime)" };
            image.material = runtime;
        }
        private void OnEnable()
        {
            voice = FindAnyObjectByType<VoiceChatManager>();
            amount = 0;
            retryAt = 0;
            Apply();
        }
        private void Update()
        {
            if (voice == null && Time.unscaledTime >= retryAt)
            {
                voice = FindAnyObjectByType<VoiceChatManager>();
                retryAt = Time.unscaledTime + 1;
            }
            bool lit = voice != null && voice.IsConnected && voice.PushToTalkHeld &&
                !voice.MicrophoneMuted && !voice.ListenOnly && !voice.SpeechExhausted;
#if UNITY_EDITOR
            if (previewInEditor && (voice == null || !voice.IsConnected))
                lit = Application.isFocused && UnityEngine.InputSystem.Keyboard.current != null &&
                    UnityEngine.InputSystem.Keyboard.current.tKey.isPressed;
#endif
            float next = Mathf.MoveTowards(amount, lit ? 1 : 0, Time.unscaledDeltaTime / Mathf.Max(.01f, fadeSeconds));
            if (Mathf.Approximately(next, amount)) return;
            amount = next;
            Apply();
        }
        private void Apply()
        {
            if (runtime == null) return;
            runtime.SetFloat(LightAmount, amount);
            // MaskableGraphic may use a stencil-derived material.
            if (image != null) image.materialForRendering.SetFloat(LightAmount, amount);
        }
        private void OnDisable() { amount = 0; Apply(); }
        private void OnDestroy()
        {
            if (image != null) image.material = original;
            if (runtime != null) Destroy(runtime);
        }
    }
}
