using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.Multiplayer
{
    /// <summary>Presentation only. All progression, role assignments and deadlines belong to the server.</summary>
    public sealed class CutsceneCarousel : MonoBehaviour
    {
        [Serializable]
        public sealed class ComicPanel
        {
            public Image image;
            [Min(0)] public float postNarrationHoldDuration = 10;
        }

        [SerializeField] private GameObject content;
        [SerializeField] private Image frameImage;
        [SerializeField] private ComicPanel[] comics;
        [SerializeField, Min(0.1f)] private float transitionDuration = 1.2f;
        [SerializeField] private Vector2 carouselRadius = new(1400, 420);
        [SerializeField] private GameObject characterSelection;
        [SerializeField] private Button personButton, briefcaseButton, leaveButton;
        [SerializeField] private Image personMarker, briefcaseMarker;
        [SerializeField] private Sprite playerOneMarkerSprite, playerTwoMarkerSprite;
        [SerializeField] private Text statusText, remainingTime;
        [SerializeField] private Image stopwatchImage;
        [SerializeField] private bool automaticallyCompleteNarration = true;
        [SerializeField] private UnityEvent<int> comicNarrationRequested = new();
        private LobbyRoleSelection selection;
        private uint requestedSequence;
        private bool hasRequestedNarration;
        private Vector2[] comicCenters;
        private Quaternion[] comicRotations;

        // Future asynchronous narration must capture the supplied sequence, not read Current later.
        public event Action<int, uint> NarrationRequested;
        public RectTransform FrameRectTransform => frameImage.rectTransform;
        public Image FrameImage => frameImage;
        public uint ActiveNarrationSequence => selection != null ? selection.Current.ComicSequence : 0;
        public int PanelCount => comics?.Length ?? 0;
        public float TransitionDuration => Mathf.Max(0.1f, transitionDuration);
        public float HoldDuration(int index) => Mathf.Max(0, comics[index].postNarrationHoldDuration);

        public void Bind(LobbyRoleSelection owner)
        {
            selection = owner;
            comicCenters = new Vector2[PanelCount];
            comicRotations = new Quaternion[PanelCount];
            for (int i = 0; i < PanelCount; i++)
            {
                comicCenters[i] = comics[i].image.rectTransform.anchoredPosition;
                comicRotations[i] = comics[i].image.rectTransform.localRotation;
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
            // TODO: present narration/text here. Async handlers must capture (comicIndex, sequence)
            // and call the two-argument completion entry point when that specific playback finishes.
            comicNarrationRequested.Invoke(comicIndex);
            NarrationRequested?.Invoke(comicIndex, sequence);
            if (automaticallyCompleteNarration) NotifyNarrationCompleted(comicIndex, sequence);
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
            statusText.text = selection.Feedback;
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
            for (int i = 0; i < PanelCount; i++)
            {
                bool incoming = i == state.ComicIndex;
                bool outgoing = entering && i == state.ComicIndex - 1;
                var image = comics[i].image;
                image.gameObject.SetActive(incoming || outgoing);
                if (!incoming && !outgoing) continue;
                // Cards turn along a quarter ellipse: incoming right -> center; outgoing center -> left.
                float angle = incoming ? (1 - eased) * Mathf.PI / 2 : -eased * Mathf.PI / 2;
                image.rectTransform.anchoredPosition = comicCenters[i] + new Vector2(
                    Mathf.Sin(angle) * carouselRadius.x, (Mathf.Cos(angle) - 1) * carouselRadius.y);
                image.rectTransform.localRotation = comicRotations[i] * Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg * 0.25f);
            }
            stopwatchImage.gameObject.SetActive(state.Stage == SessionStage.CharacterSelection && state.CountdownActive);
            remainingTime.text = state.CountdownActive
                ? Math.Ceiling(Math.Max(0, state.CountdownEndsAt - selection.ServerNow)).ToString("0") : string.Empty;
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
