using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Game.Multiplayer
{
    public enum CarouselRotationMode { ComicOnly, ComicAndFrame }

    /// <summary>Presentation only. All progression, role assignments and deadlines belong to the server.</summary>
    public sealed class CutsceneCarousel : MonoBehaviour
    {
        [Serializable]
        public sealed class NarrationSegment
        {
            [TextArea(2, 5)] public string text;
            [Min(0.1f)] public float duration = 4;
        }

        [Serializable]
        public sealed class ComicPanel
        {
            public Image image;
            [Tooltip("Authored frame behind this comic in ComicAndFrame mode. Comic 3 uses the main Frame Image.")]
            public Image rotatingFrame;
            public NarrationSegment[] narration = Array.Empty<NarrationSegment>();
            [Min(0)] public float postNarrationHoldDuration = 10;
        }

        [SerializeField] private GameObject content;
        [SerializeField] private Image frameImage;
        [SerializeField] private ComicPanel[] comics;
        [SerializeField, Min(0.1f)] private float transitionDuration = 1.2f;
        [SerializeField] private CarouselRotationMode rotationMode = CarouselRotationMode.ComicOnly;
        [SerializeField, Min(0.1f)] private float selectionScaleDuration = 0.6f;
        [SerializeField] private Vector2 carouselRadius = new(1400, 420);
        [SerializeField] private GameObject characterSelection;
        [SerializeField] private Button personButton, briefcaseButton, leaveButton;
        [SerializeField] private Image personMarker, briefcaseMarker;
        [SerializeField] private Sprite playerOneMarkerSprite, playerTwoMarkerSprite;
        [FormerlySerializedAs("statusText"), SerializeField] private Text narrationText;
        [SerializeField] private Text remainingTime;
        [SerializeField] private Image stopwatchImage;
        [SerializeField] private UnityEvent<int> comicNarrationRequested = new();
        private LobbyRoleSelection selection;
        private uint requestedSequence;
        private bool hasRequestedNarration;
        private Vector2[] comicCenters;
        private Quaternion[] comicRotations;
        private Vector3[] comicScales;
        private Vector2[] frameCenters;
        private Quaternion[] frameRotations;
        private Vector3[] frameScales;
        private Vector2 mainFrameCenter;
        private Quaternion mainFrameRotation;
        private Vector3 mainFrameScale;

        // Future asynchronous narration must capture the supplied sequence, not read Current later.
        public event Action<int, uint> NarrationRequested;
        public RectTransform FrameRectTransform => frameImage.rectTransform;
        public Image FrameImage => frameImage;
        public uint ActiveNarrationSequence => selection != null ? selection.Current.ComicSequence : 0;
        public int PanelCount => comics?.Length ?? 0;
        public float TransitionDuration => Mathf.Max(0.1f, transitionDuration);
        public float SelectionScaleDuration => Mathf.Max(0.1f, selectionScaleDuration);
        public float HoldDuration(int index) => Mathf.Max(0, comics[index].postNarrationHoldDuration);
        public int SegmentCount(int index) => comics[index].narration?.Length ?? 0;
        public float SegmentDuration(int comic, int segment) => segment < SegmentCount(comic)
            ? Mathf.Max(0.1f, comics[comic].narration[segment].duration) : 0;

        public void Bind(LobbyRoleSelection owner)
        {
            selection = owner;
            comicCenters = new Vector2[PanelCount];
            comicRotations = new Quaternion[PanelCount];
            comicScales = new Vector3[PanelCount];
            frameCenters = new Vector2[PanelCount];
            frameRotations = new Quaternion[PanelCount];
            frameScales = new Vector3[PanelCount];
            mainFrameCenter = FrameRectTransform.anchoredPosition;
            mainFrameRotation = FrameRectTransform.localRotation;
            mainFrameScale = FrameRectTransform.localScale;
            for (int i = 0; i < PanelCount; i++)
            {
                comicCenters[i] = comics[i].image.rectTransform.anchoredPosition;
                comicRotations[i] = comics[i].image.rectTransform.localRotation;
                comicScales[i] = comics[i].image.rectTransform.localScale;
                if (comics[i].rotatingFrame != null)
                {
                    var frame = comics[i].rotatingFrame.rectTransform;
                    frameCenters[i] = frame.anchoredPosition;
                    frameRotations[i] = frame.localRotation;
                    frameScales[i] = frame.localScale;
                }
            }
            selection.Changed += Refresh;
            personButton.onClick.AddListener(SelectPerson);
            briefcaseButton.onClick.AddListener(SelectBriefcase);
            leaveButton.onClick.AddListener(Leave);
            Refresh();
        }

        private void SelectPerson() => Toggle(LobbyRole.Traveler);
        private void SelectBriefcase() => Toggle(LobbyRole.Hacker);
        private void Toggle(LobbyRole role)
        {
            if (selection == null || !selection.IsSpawned || selection.HasPendingRequest ||
                selection.Current.Stage != SessionStage.CharacterSelection ||
                selection.Current.IsOccupied(role, selection.NetworkManager.LocalClientId)) return;
            if (selection.TryGetLocalPlayer(out var local))
                selection.SelectRole(local.Role == role ? LobbyRole.Unassigned : role);
        }
        private void Leave() => selection.NetworkManager.GetComponent<NetworkSessionManager>().LeaveRoom();

        private void Update()
        {
            if (selection == null || !selection.IsSpawned) return;
            RefreshVisuals();
            var state = selection.Current;
            if (state.Stage == SessionStage.ComicNarration &&
                (!hasRequestedNarration || requestedSequence != state.ComicSequence))
            {
                hasRequestedNarration = true;
                requestedSequence = state.ComicSequence;
                // Entering is already complete on the server; snap the center pose before requesting text/audio.
                OnComicNarrationRequested(state.ComicIndex);
            }
        }

        public void OnComicNarrationRequested(int comicIndex)
        {
            if (selection == null || selection.Current.Stage != SessionStage.ComicNarration ||
                selection.Current.ComicIndex != comicIndex) return;
            uint sequence = selection.Current.ComicSequence;
            // Optional presentation hooks only; these do not advance the authoritative segment clock.
            comicNarrationRequested.Invoke(comicIndex);
            NarrationRequested?.Invoke(comicIndex, sequence);
        }

        public void NotifyNarrationCompleted(int comicIndex, uint sequence) =>
            selection?.NotifyNarrationCompleted(comicIndex, sequence);

        private void Refresh()
        {
            if (selection == null || !selection.IsSpawned) return;
            var state = selection.Current;
            content.SetActive(state.Stage != SessionStage.Lobby && state.Stage != SessionStage.Gameplay);
            characterSelection.SetActive(state.Stage == SessionStage.CharacterSelection);
            bool canSelect = state.Stage == SessionStage.CharacterSelection && !selection.HasPendingRequest &&
                selection.TryGetLocalPlayer(out _);
            personButton.interactable = canSelect && !state.IsOccupied(LobbyRole.Traveler, selection.NetworkManager.LocalClientId);
            briefcaseButton.interactable = canSelect && !state.IsOccupied(LobbyRole.Hacker, selection.NetworkManager.LocalClientId);
            RefreshMarker(personMarker, state, LobbyRole.Traveler);
            RefreshMarker(briefcaseMarker, state, LobbyRole.Hacker);
            RefreshVisuals();
        }

        private void RefreshMarker(Image marker, LobbyRoleState state, LobbyRole role)
        {
            // Authoritative state alone controls markers; hover and speculative clicks never do.
            Sprite sprite = state.Host.Connected && state.Host.Role == role ? playerOneMarkerSprite :
                state.Client.Connected && state.Client.Role == role ? playerTwoMarkerSprite : null;
            marker.sprite = sprite;
            marker.gameObject.SetActive(sprite != null);
        }

        private void RefreshVisuals()
        {
            var state = selection.Current;
            bool entering = state.Stage == SessionStage.ComicEntering;
            float progress = entering ? Mathf.Clamp01((float)((selection.ServerNow - state.StageStartedAt) /
                Math.Max(0.001, state.StageEndsAt - state.StageStartedAt))) : 1;
            float eased = Mathf.SmoothStep(0, 1, progress);
            if (rotationMode == CarouselRotationMode.ComicAndFrame)
                AnimateComicAndFrameRotation(state, entering, eased);
            else AnimateComicOnlyRotation(state, entering, eased);

            bool selecting = state.Stage is SessionStage.SelectionEntering or SessionStage.CharacterSelection;
            float selectionProgress = state.Stage == SessionStage.CharacterSelection ? 1 : selecting
                ? Mathf.Clamp01((float)((selection.ServerNow - state.StageStartedAt) /
                    Math.Max(0.001, state.StageEndsAt - state.StageStartedAt))) : 0;
            if (selecting)
            {
                float scaleEase = Mathf.SmoothStep(0, 1, selectionProgress);
                comics[PanelCount - 1].image.rectTransform.localScale = Vector3.Lerp(comicScales[PanelCount - 1], Vector3.one, scaleEase);
                FrameRectTransform.localScale = Vector3.Lerp(mainFrameScale, Vector3.one, scaleEase);
            }

            // Render the latest replicated segment, never run a second local progression timer.
            bool showNarration = state.Stage is SessionStage.ComicNarration or SessionStage.ComicHold;
            string text = showNarration && state.ComicIndex >= 0 && state.ComicIndex < PanelCount &&
                state.NarrationSegmentIndex >= 0 && state.NarrationSegmentIndex < SegmentCount(state.ComicIndex)
                ? comics[state.ComicIndex].narration[state.NarrationSegmentIndex].text : string.Empty;
            if (narrationText.text != text) narrationText.text = text;
            narrationText.gameObject.SetActive(showNarration);
            stopwatchImage.gameObject.SetActive(state.Stage == SessionStage.CharacterSelection && state.CountdownActive);
            remainingTime.text = state.CountdownActive
                ? Math.Ceiling(Math.Max(0, state.CountdownEndsAt - selection.ServerNow)).ToString("0") : string.Empty;
        }

        private void AnimateComicOnlyRotation(LobbyRoleState state, bool entering, float eased)
        {
            ResetFrames();
            frameImage.gameObject.SetActive(true);
            FrameRectTransform.SetSiblingIndex(1); // Stationary frame behind all comics, above the background.
            AnimateComics(state, entering, eased, false);
        }

        private void AnimateComicAndFrameRotation(LobbyRoleState state, bool entering, float eased)
        {
            ResetFrames();
            AnimateComics(state, entering, eased, true);
            // Farther pair first, nearer pair last; each frame immediately precedes its comic.
            int incoming = Mathf.Clamp(state.ComicIndex, 0, PanelCount - 1);
            int outgoing = entering ? incoming - 1 : -1;
            if (outgoing >= 0 && eased < 0.5f) { OrderPair(incoming); OrderPair(outgoing); }
            else { if (outgoing >= 0) OrderPair(outgoing); OrderPair(incoming); }
        }

        private void OrderPair(int index)
        {
            // Insert before narration/selection, never above those controls.
            int slot = Mathf.Min(narrationText.transform.GetSiblingIndex(), characterSelection.transform.GetSiblingIndex());
            if (comics[index].rotatingFrame != null) comics[index].rotatingFrame.transform.SetSiblingIndex(slot - 1);
            comics[index].image.transform.SetSiblingIndex(Mathf.Min(narrationText.transform.GetSiblingIndex(),
                characterSelection.transform.GetSiblingIndex()) - 1);
        }

        private void ResetFrames()
        {
            FrameRectTransform.anchoredPosition = mainFrameCenter;
            FrameRectTransform.localRotation = mainFrameRotation;
            FrameRectTransform.localScale = mainFrameScale;
            frameImage.gameObject.SetActive(false);
            for (int i = 0; i < PanelCount; i++)
                if (comics[i].rotatingFrame != null)
                {
                    var frame = comics[i].rotatingFrame.rectTransform;
                    frame.anchoredPosition = frameCenters[i];
                    frame.localRotation = frameRotations[i];
                    frame.localScale = frameScales[i];
                    frame.gameObject.SetActive(false);
                }
        }

        private void AnimateComics(LobbyRoleState state, bool entering, float eased, bool withFrames)
        {
            for (int i = 0; i < PanelCount; i++)
            {
                bool incoming = i == state.ComicIndex;
                bool outgoing = entering && i == state.ComicIndex - 1;
                var image = comics[i].image;
                image.rectTransform.localScale = comicScales[i];
                image.gameObject.SetActive(incoming || outgoing);
                if (!incoming && !outgoing) continue;
                // Cards turn along a quarter ellipse: incoming right -> center; outgoing center -> left.
                float angle = incoming ? (1 - eased) * Mathf.PI / 2 : -eased * Mathf.PI / 2;
                image.rectTransform.anchoredPosition = comicCenters[i] + new Vector2(
                    Mathf.Sin(angle) * carouselRadius.x, (Mathf.Cos(angle) - 1) * carouselRadius.y);
                image.rectTransform.localRotation = comicRotations[i] * Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg * 0.25f);
                if (withFrames && comics[i].rotatingFrame != null)
                {
                    float depth = Mathf.Lerp(0.85f, 1, Mathf.Cos(angle));
                    Quaternion turn = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg * 0.25f);
                    image.rectTransform.localScale = comicScales[i] * depth;
                    var frame = comics[i].rotatingFrame.rectTransform;
                    frame.gameObject.SetActive(true);
                    frame.anchoredPosition = image.rectTransform.anchoredPosition +
                        (Vector2)(turn * (Vector3)((frameCenters[i] - comicCenters[i]) * depth));
                    frame.localRotation = turn * frameRotations[i];
                    frame.localScale = frameScales[i] * depth;
                }
            }
        }

        private void OnDestroy()
        {
            if (selection != null) selection.Changed -= Refresh;
            if (personButton != null) personButton.onClick.RemoveListener(SelectPerson);
            if (briefcaseButton != null) briefcaseButton.onClick.RemoveListener(SelectBriefcase);
            if (leaveButton != null) leaveButton.onClick.RemoveListener(Leave);
        }
    }
}
