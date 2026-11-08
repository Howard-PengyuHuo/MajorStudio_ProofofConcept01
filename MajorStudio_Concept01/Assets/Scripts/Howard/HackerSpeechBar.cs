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
        private int displayedTenth = -1;
        private float displayedLimit = -1;
        private bool displayedHeld, displayedExhausted, disconnected;
        private void OnEnable()
        {
            voice = FindAnyObjectByType<VoiceChatManager>();
            displayedTenth = -1;
            disconnected = false;
        }
        private void Update()
        {
            if (voice == null)
            {
                if (!disconnected && remainingText != null) remainingText.text = "VOICE TIME / CONNECT TO A ROOM";
                disconnected = true;
                return;
            }
            if (remainingBar != null)
            {
                remainingBar.maxValue = voice.SpeechLimit;
                remainingBar.SetValueWithoutNotify(voice.SpeechRemaining);
            }
            int tenth = Mathf.RoundToInt(voice.SpeechRemaining * 10);
            if (tenth == displayedTenth && displayedLimit == voice.SpeechLimit &&
                displayedHeld == voice.PushToTalkHeld && displayedExhausted == voice.SpeechExhausted) return;
            displayedTenth = tenth;
            displayedLimit = voice.SpeechLimit;
            displayedHeld = voice.PushToTalkHeld;
            displayedExhausted = voice.SpeechExhausted;
            if (remainingText != null) remainingText.text = voice.SpeechExhausted ? "VOICE TIME  0.0s / MIC LOCKED" :
                $"{(voice.PushToTalkHeld ? "T / TALKING" : "HOLD T")}  {voice.SpeechRemaining:F1}s / {voice.SpeechLimit:0.#}s";
        }
    }
}
