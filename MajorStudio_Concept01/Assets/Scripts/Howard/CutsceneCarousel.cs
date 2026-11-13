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
        [SerializeField] private GameObject letterboxBackground;
        [SerializeField] private Image frameImage;
        [SerializeField] private ComicPanel[] comics;
        [SerializeField, Min(0.1f)] private float transitionDuration = 1.2f;
        [SerializeField] private CarouselRotationMode rotationMode = CarouselRotationMode.ComicOnly;
        [SerializeField, Min(0.1f)] private float selectionScaleDuration = 0.6f;
        [SerializeField] private Vector2 carouselRadius = new(1400, 420);
        [SerializeField] private float readingVerticalOffset = 80;
        [SerializeField] private GameObject characterSelection;
        [SerializeField] private Button personButton, briefcaseButton, leaveButton;
        [SerializeField] private Image personMarker, briefcaseMarker;
        [SerializeField] private Sprite playerOneMarkerSprite, playerTwoMarkerSprite;
        [FormerlySerializedAs("statusText"), SerializeField] private Text narrationText;
        [SerializeField] private Text remainingTime;
        [SerializeField] private Text navigationHint;
        [SerializeField] private Image stopwatchImage;
        [SerializeField] private UnityEvent<int> comicNarrationRequested = new();
        private LobbyRoleSelection selection;
        private uint requestedSequence;
        private bool hasRequestedNarration;
        private int requestedComic = -1;
        private Vector2[] comicCenters;
        private Quaternion[] comicRotations;
        private Vector3[] comicScales;
        private Vector2[] frameCenters;
        private Quaternion[] frameRotations;
        private Vector3[] frameScales;
        private Vector2 mainFrameCenter;
        private Quaternion mainFrameRotation;
        private Vector3 mainFrameScale;
        private uint localSequence;
        private bool localAtSelection;
        private double localSelectionStartedAt;
        private int localPage = -1;
        private int outgoingComic = -1;
        private int turnDirection = 1;
        private double localTurnStartedAt;

        // Page position is per player. Role ownership and the start countdown remain shared.
        private LobbyRoleState PresentationState()
        {
            var state = selection.Current;
            if (state.Stage == SessionStage.Lobby || state.Stage == SessionStage.Gameplay)
            {
                localAtSelection = false;
                localPage = -1;
                return state;
            }
            if (!state.TryGetPlayer(selection.NetworkManager.LocalClientId, out var player)) return state;
            if (localSequence != state.ComicSequence)
            {
                localSequence = state.ComicSequence;
                localAtSelection = false;
                localPage = -1;
            }
            if (localPage != player.CutscenePage)
            {
                int previous = localPage;
                localPage = player.CutscenePage;
                int target = Mathf.Clamp(localPage, 0, PanelCount - 1);
                outgoingComic = previous >= 0 && Mathf.Clamp(previous, 0, PanelCount - 1) != target
                    ? Mathf.Clamp(previous, 0, PanelCount - 1) : -1;
                turnDirection = previous < 0 || localPage > previous ? 1 : -1;
                localTurnStartedAt = outgoingComic >= 0 || previous < 0 ? selection.ServerNow : double.NegativeInfinity;
            }
            bool arrived = player.CutscenePage >= PanelCount;
            if (arrived && !localAtSelection) localSelectionStartedAt = selection.ServerNow;
            localAtSelection = arrived;
            state.ComicIndex = Mathf.Clamp(player.CutscenePage, 0, PanelCount - 1);
            state.NarrationSegmentIndex = 0;
            state.Stage = !arrived ? SessionStage.ComicNarration :
                selection.ServerNow - localSelectionStartedAt < SelectionScaleDuration
                    ? SessionStage.SelectionEntering : SessionStage.CharacterSelection;
            if (!arrived && selection.ServerNow - localTurnStartedAt < TransitionDuration)
            {
                state.Stage = SessionStage.ComicEntering;
                state.StageStartedAt = localTurnStartedAt;
                state.StageEndsAt = localTurnStartedAt + TransitionDuration;
            }
            if (arrived)
            {
                state.StageStartedAt = localSelectionStartedAt;
                state.StageEndsAt = localSelectionStartedAt + SelectionScaleDuration;
            }
            return state;
        }

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
            CacheAuthoredLayout();
            selection.Changed += Refresh;
            personButton.onClick.AddListener(SelectPerson);
            briefcaseButton.onClick.AddListener(SelectBriefcase);
            leaveButton.onClick.AddListener(Leave);
            Refresh();
        }

        private void CacheAuthoredLayout()
        {
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
        }

        // Used only by the standalone preview scene; shares the game's actual animation.
        public void InitializePreview() => CacheAuthoredLayout();

        public void RenderPreview(int page, int previous, int direction, double startedAt)
        {
            double now = Time.unscaledTimeAsDouble;
            bool selecting = page >= PanelCount;
            float duration = selecting ? SelectionScaleDuration : TransitionDuration;
            var state = new LobbyRoleState
            {
                ComicIndex = Mathf.Clamp(page, 0, PanelCount - 1),
                Stage = selecting
                    ? (now - startedAt < duration ? SessionStage.SelectionEntering : SessionStage.CharacterSelection)
                    : (now - startedAt < duration ? SessionStage.ComicEntering : SessionStage.ComicNarration),
                StageStartedAt = startedAt, StageEndsAt = startedAt + duration
            };
            outgoingComic = previous >= 0 && previous != state.ComicIndex ? Mathf.Min(previous, PanelCount - 1) : -1;
            turnDirection = direction;
            SetStageVisibility(state.Stage);
            personButton.interactable = briefcaseButton.interactable = false;
            leaveButton.gameObject.SetActive(false);
            personMarker.gameObject.SetActive(false);
            briefcaseMarker.gameObject.SetActive(false);
            if (navigationHint != null)
            {
                navigationHint.gameObject.SetActive(true);
                navigationHint.text = "USE LEFT/RIGHT ARROW KEY TO SWITCH";
            }
            RenderVisuals(state, now);
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
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame) selection.NavigateCutscene(-1);
                else if (keyboard.rightArrowKey.wasPressedThisFrame) selection.NavigateCutscene(1);
            }
            Refresh();
            var state = PresentationState();
            if (state.Stage == SessionStage.ComicNarration &&
                (!hasRequestedNarration || requestedSequence != state.ComicSequence || requestedComic != state.ComicIndex))
            {
                hasRequestedNarration = true;
                requestedSequence = state.ComicSequence;
                requestedComic = state.ComicIndex;
                // Entering is already complete on the server; snap the center pose before requesting text/audio.
                OnComicNarrationRequested(state.ComicIndex);
            }
        }

        public void OnComicNarrationRequested(int comicIndex)
        {
            if (selection == null) return;
            var state = PresentationState();
            if (state.Stage != SessionStage.ComicNarration || state.ComicIndex != comicIndex) return;
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
            var state = PresentationState();
            SetStageVisibility(state.Stage);
            bool canSelect = state.Stage == SessionStage.CharacterSelection &&
                selection.Current.Stage == SessionStage.CharacterSelection && !selection.HasPendingRequest &&
                selection.TryGetLocalPlayer(out _);
            personButton.interactable = canSelect && !state.IsOccupied(LobbyRole.Traveler, selection.NetworkManager.LocalClientId);
            briefcaseButton.interactable = canSelect && !state.IsOccupied(LobbyRole.Hacker, selection.NetworkManager.LocalClientId);
            RefreshMarker(personMarker, state, LobbyRole.Traveler);
            RefreshMarker(briefcaseMarker, state, LobbyRole.Hacker);
            if (navigationHint != null)
            {
                bool reading = selection.Current.Stage == SessionStage.ComicNarration;
                navigationHint.gameObject.SetActive(reading);
                navigationHint.text = localAtSelection
                    ? "WAITING FOR THE OTHER PLAYER. LEFT ARROW TO GO BACK."
                    : "USE LEFT / RIGHT ARROW KEYS TO SWITCH";
            }
            RefreshVisuals();
        }

        private void SetStageVisibility(SessionStage stage)
        {
            bool showCutscene = stage != SessionStage.Lobby && stage != SessionStage.Gameplay;
            content.SetActive(showCutscene);
            if (letterboxBackground != null) letterboxBackground.SetActive(showCutscene);
            characterSelection.SetActive(stage == SessionStage.CharacterSelection);
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
            var state = PresentationState();
            RenderVisuals(state, selection.ServerNow);
        }

        private void RenderVisuals(LobbyRoleState state, double now)
        {
            bool entering = state.Stage == SessionStage.ComicEntering;
            float progress = entering ? Mathf.Clamp01((float)((now - state.StageStartedAt) /
                Math.Max(0.001, state.StageEndsAt - state.StageStartedAt))) : 1;
            float eased = Mathf.SmoothStep(0, 1, progress);
            if (rotationMode == CarouselRotationMode.ComicAndFrame)
                AnimateComicAndFrameRotation(state, entering, eased);
            else AnimateComicOnlyRotation(state, entering, eased);

            bool selecting = state.Stage is SessionStage.SelectionEntering or SessionStage.CharacterSelection;
            float selectionProgress = state.Stage == SessionStage.CharacterSelection ? 1 : selecting
                ? Mathf.Clamp01((float)((now - state.StageStartedAt) /
                    Math.Max(0.001, state.StageEndsAt - state.StageStartedAt))) : 0;
            if (selecting)
            {
                float scaleEase = Mathf.SmoothStep(0, 1, selectionProgress);
                comics[PanelCount - 1].image.rectTransform.localScale = Vector3.Lerp(comicScales[PanelCount - 1], Vector3.one, scaleEase);
                FrameRectTransform.localScale = Vector3.Lerp(mainFrameScale, Vector3.one, scaleEase);
            }
            // Move the whole illustrated pair up while reading, then settle back for selection.
            float readingOffset = state.Stage is SessionStage.Lobby or SessionStage.Gameplay ? 0 :
                readingVerticalOffset * (1 - Mathf.SmoothStep(0, 1, selectionProgress));
            for (int i = 0; i < PanelCount; i++)
            {
                if (comics[i].image.gameObject.activeSelf)
                    comics[i].image.rectTransform.anchoredPosition += Vector2.up * readingOffset;
                if (comics[i].rotatingFrame != null)
                    comics[i].rotatingFrame.rectTransform.anchoredPosition += Vector2.up * readingOffset;
            }
            if (rotationMode == CarouselRotationMode.ComicOnly)
                FrameRectTransform.anchoredPosition += Vector2.up * readingOffset;

            // Render the latest replicated segment, never run a second local progression timer.
            bool showNarration = state.Stage is SessionStage.ComicNarration or SessionStage.ComicHold;
            string text = showNarration && state.ComicIndex >= 0 && state.ComicIndex < PanelCount &&
                state.NarrationSegmentIndex >= 0 && state.NarrationSegmentIndex < SegmentCount(state.ComicIndex)
                ? comics[state.ComicIndex].narration[state.NarrationSegmentIndex].text : string.Empty;
            if (narrationText != null)
            {
                if (narrationText.text != text) narrationText.text = text;
                narrationText.gameObject.SetActive(showNarration);
            }
            stopwatchImage.gameObject.SetActive(state.Stage == SessionStage.CharacterSelection && state.CountdownActive);
            remainingTime.text = state.CountdownActive
                ? Math.Ceiling(Math.Max(0, state.CountdownEndsAt - now)).ToString("0") : string.Empty;
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
            int outgoing = entering ? outgoingComic : -1;
            if (outgoing >= 0 && eased < 0.5f) { OrderPair(incoming); OrderPair(outgoing); }
            else { if (outgoing >= 0) OrderPair(outgoing); OrderPair(incoming); }
        }

        private void OrderPair(int index)
        {
            // Insert before narration/selection, never above those controls.
            int narrationSlot = narrationText != null ? narrationText.transform.GetSiblingIndex() : int.MaxValue;
            int slot = Mathf.Min(narrationSlot, characterSelection.transform.GetSiblingIndex());
            if (comics[index].rotatingFrame != null) comics[index].rotatingFrame.transform.SetSiblingIndex(slot - 1);
            narrationSlot = narrationText != null ? narrationText.transform.GetSiblingIndex() : int.MaxValue;
            comics[index].image.transform.SetSiblingIndex(Mathf.Min(narrationSlot,
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
                bool outgoing = entering && i == outgoingComic;
                var image = comics[i].image;
                image.rectTransform.localScale = comicScales[i];
                image.gameObject.SetActive(incoming || outgoing);
                if (!incoming && !outgoing) continue;
                // Cards turn along a quarter ellipse: incoming right -> center; outgoing center -> left.
                float angle = (incoming ? (1 - eased) : -eased) * Mathf.PI / 2 * turnDirection;
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
