using TMPro;
using UnityEngine;

namespace Game.Multiplayer
{
    // Presentation only: hiding or switching this panel never resets the voice allowance.
    public sealed class HackerSpeechBar : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Slider remainingBar;
        [SerializeField] private TMP_Text remainingText;
        private VoiceChatManager voice;
        private void OnEnable() => voice = FindFirstObjectByType<VoiceChatManager>();
        private void Update()
        {
            if (voice == null)
            {
                remainingText.text = "VOICE TIME / CONNECT TO A ROOM";
                return;
            }
            remainingBar.maxValue = voice.SpeechLimit;
            remainingBar.SetValueWithoutNotify(voice.SpeechRemaining);
            remainingText.text = voice.SpeechExhausted ? "VOICE TIME  0.0s / MIC LOCKED" :
                $"VOICE TIME  {voice.SpeechRemaining:F1}s / {voice.SpeechLimit:0.#}s";
        }
    }
}
