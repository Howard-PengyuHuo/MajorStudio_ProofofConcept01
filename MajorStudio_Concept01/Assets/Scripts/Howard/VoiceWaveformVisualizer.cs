using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Multiplayer
{
    public enum VoiceVisualizationState { Inactive, Silent, Speaking, Disconnected }

    /// <summary>Energy-driven activity bars, not a raw-audio waveform or frequency spectrum.</summary>
    [DisallowMultipleComponent]
    public sealed class VoiceWaveformVisualizer : MonoBehaviour
    {
        [SerializeField] private VoiceChatManager voice;
        [SerializeField] private Image background;
        [SerializeField] private RectTransform waveformRegion;
        [SerializeField] private Image[] bars;
        [SerializeField, Range(1, 32)] private int barCount = 28;
        [SerializeField, Min(0)] private float minimumHeight = 2;
        [SerializeField, Min(0)] private float maximumHeight = 72;
        [SerializeField, Min(0)] private float audioGain = 3;
        [SerializeField, Min(0.1f)] private float smoothingSpeed = 18;
        [SerializeField, Min(0.1f)] private float attackSpeed = 24;
        [SerializeField, Min(0.1f)] private float releaseSpeed = 7;
        [SerializeField] private Color barColor = new(0.15f, 0.22f, 0.14f, 1);
        [SerializeField] private Color idleColor = new(0.28f, 0.32f, 0.25f, 0.6f);
        private VivoxParticipant participant;
        private RectTransform[] rects;
        private float[] heights;
        private Vector2 areaMin, areaMax;
        private float energy, envelope, resolveAt;
        public VoiceVisualizationState State { get; private set; } = VoiceVisualizationState.Disconnected;
        public bool IsTransmitting { get; private set; }
        public bool VisualizingSelf { get; private set; }
        public float DisplayedEnergy => envelope;

        private void Awake()
        {
            bars ??= System.Array.Empty<Image>();
            rects = new RectTransform[bars.Length];
            heights = new float[bars.Length];
            for (int i = 0; i < bars.Length; i++) if (bars[i] != null) rects[i] = bars[i].rectTransform;
            if (waveformRegion != null)
            {
                // The prefab's editable anchors define the region within the artwork.
                areaMin = waveformRegion.anchorMin;
                areaMax = waveformRegion.anchorMax;
            }
        }

        private void OnEnable() { resolveAt = 0; ResetDisplay(); }
        private void Update()
        {
            if (voice == null && Time.unscaledTime >= resolveAt)
            {
                voice = FindAnyObjectByType<VoiceChatManager>();
                resolveAt = Time.unscaledTime + 1;
            }
            bool connected = voice != null && voice.VoiceChannelAvailable;
            VivoxParticipant source = null;
            bool transmitting = false;
            bool open = connected && voice.TryGetCommsParticipant(out source, out transmitting);
            BindParticipant(open ? source : null);
            IsTransmitting = open && transmitting;
            Present(connected, open, energy, Time.unscaledDeltaTime);
        }

        private void BindParticipant(VivoxParticipant next)
        {
            if (ReferenceEquals(participant, next)) return;
            if (participant != null)
            {
                participant.ParticipantAudioEnergyChanged -= ReadEnergy;
                participant.ParticipantSpeechDetected -= ReadEnergy;
                participant.ParticipantMuteStateChanged -= ReadEnergy;
                participant.ParticipantAudioStateChanged -= ReadEnergy;
            }
            participant = next;
            energy = 0;
            VisualizingSelf = participant != null && participant.IsSelf;
            if (participant == null) return;
            participant.ParticipantAudioEnergyChanged += ReadEnergy;
            participant.ParticipantSpeechDetected += ReadEnergy;
            participant.ParticipantMuteStateChanged += ReadEnergy;
            participant.ParticipantAudioStateChanged += ReadEnergy;
            ReadEnergy();
        }

        private void ReadEnergy()
        {
            double value = participant != null && participant.IsInAudio && !participant.IsMuted && participant.SpeechDetected
                ? participant.AudioEnergy : 0;
            energy = double.IsNaN(value) || double.IsInfinity(value) ? 0 : Mathf.Clamp01((float)value);
        }

        private void Present(bool connected, bool open, float actualEnergy, float dt)
        {
            State = !connected ? VoiceVisualizationState.Disconnected : !open ? VoiceVisualizationState.Inactive :
                actualEnergy > 0 ? VoiceVisualizationState.Speaking : VoiceVisualizationState.Silent;
            if (waveformRegion == null || rects == null) return;
            bool visible = connected && open;
            if (waveformRegion.gameObject.activeSelf != visible) waveformRegion.gameObject.SetActive(visible);
            if (!visible) { envelope = 0; System.Array.Clear(heights, 0, heights.Length); return; }
            FitRegionToArtwork();
            float target = Mathf.Clamp01(actualEnergy * audioGain);
            envelope = Smooth(envelope, target, target > envelope ? attackSpeed : releaseSpeed, dt);
            float max = Mathf.Max(0, Mathf.Min(Mathf.Max(minimumHeight, maximumHeight), waveformRegion.rect.height));
            float min = Mathf.Clamp(minimumHeight, 0, max);
            int count = Mathf.Clamp(barCount, 1, Mathf.Max(1, bars.Length));
            for (int i = 0; i < bars.Length; i++)
            {
                if (rects[i] == null || bars[i] == null) continue;
                bool active = i < count;
                if (bars[i].gameObject.activeSelf != active) bars[i].gameObject.SetActive(active);
                if (!active) continue;
                rects[i].anchorMin = rects[i].anchorMax = new Vector2((i + 0.5f) / count, 0.5f);
                // Variation is deterministic and multiplied by actual energy: silence has no generated peaks.
                float variation = 0.32f + 0.68f * Mathf.Abs(Mathf.Sin(i * 2.39996f + Time.unscaledTime * 4));
                heights[i] = Smooth(Mathf.Max(min, heights[i]), Mathf.Lerp(min, max, envelope * variation), smoothingSpeed, dt);
                rects[i].sizeDelta = new Vector2(Mathf.Max(1, waveformRegion.rect.width / count * 0.4f), heights[i]);
                bars[i].color = Color.Lerp(idleColor, barColor, envelope);
            }
        }

        private void FitRegionToArtwork()
        {
            if (background == null) return;
            Rect bounds = background.GetPixelAdjustedRect();
            var sprite = background.overrideSprite;
            if (background.preserveAspect && sprite != null && sprite.rect.height > 0 && bounds.height > 0)
            {
                float aspect = sprite.rect.width / sprite.rect.height;
                Vector2 size = bounds.size;
                if (size.x / size.y > aspect) size.x = size.y * aspect;
                else size.y = size.x / aspect;
                bounds.position += Vector2.Scale(bounds.size - size, background.rectTransform.pivot);
                bounds.size = size;
            }
            // Account for Image.preserveAspect letterboxing without changing the original Image/RectTransform.
            waveformRegion.anchorMin = waveformRegion.anchorMax = background.rectTransform.pivot;
            waveformRegion.anchoredPosition = bounds.min + Vector2.Scale(bounds.size, (areaMin + areaMax) * 0.5f);
            waveformRegion.sizeDelta = Vector2.Scale(bounds.size, areaMax - areaMin);
        }

        private static float Smooth(float value, float target, float speed, float dt) =>
            Mathf.Lerp(value, target, 1 - Mathf.Exp(-Mathf.Max(0.1f, speed) * Mathf.Max(0, dt)));
        private void ResetDisplay()
        {
            energy = envelope = 0;
            IsTransmitting = false;
            State = VoiceVisualizationState.Disconnected;
            if (heights != null) System.Array.Clear(heights, 0, heights.Length);
            if (waveformRegion != null) waveformRegion.gameObject.SetActive(false);
        }
        private void OnDisable() { BindParticipant(null); ResetDisplay(); }
    }
}
