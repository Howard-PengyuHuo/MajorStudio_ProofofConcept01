using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.Multiplayer
{
    // One local voice session alongside the persistent lobby NetworkSessionManager.
    // Audio uses Vivox; no microphone data is sent through gameplay RPCs.
    [DisallowMultipleComponent, RequireComponent(typeof(NetworkSessionManager))]
    public sealed class VoiceChatManager : MonoBehaviour
    {
        [SerializeField] private bool microphoneMuted;
        [SerializeField, Min(0.1f)] private float hackerSpeechSeconds = 10f;
        public float SpeechLimit => hackerSpeechSeconds;
        public float SpeechRemaining => Mathf.Max(0, hackerSpeechSeconds - speechUsed);
        public bool SpeechExhausted => speechBudgetActive && SpeechRemaining <= 0;
        private float speechUsed;
        private uint speechRound;
        private bool speechBudgetActive;
        public string Status { get; private set; } = "Voice: offline";
        public bool IsConnected { get; private set; }
        public int ParticipantCount { get; private set; }
        public bool MicrophoneMuted => microphoneMuted;
        public bool PushToTalkRequired => session != null && session.IsInGame;
        public bool PushToTalkHeld { get; private set; }
        private bool applicationPaused;
        public bool ListenOnly => session != null && session.IsInGame &&
            (session.RoleSelection == null ||
             !session.RoleSelection.TryGetRole(session.LocalClientId, out var role) ||
             role != CarryOn.Taylor.PlayerRole.Hacker);
        public int InputVolume { get; private set; }
        public int OutputVolume { get; private set; }
        public int TeammateVolume { get; private set; }
        private Task preparation;
        public event Action Changed;

        private NetworkSessionManager session;
        private IVivoxService service;
        private string desiredChannel = "", connectedChannel = "", joiningChannel = "";
        private TaskCompletionSource<bool> channelJoined;
        private bool busy, subscribed, stopped;
        private int retries;
        private float retryAt;

        private void Awake() => session = GetComponent<NetworkSessionManager>();

        private void OnEnable()
        {
            stopped = false;
            session.StateChanged += OnSessionChanged;
            session.JoinCodeChanged += OnCodeChanged;
            session.LobbyChanged += OnRoleChanged;
            RefreshRoom();
        }

        private void OnSessionChanged(ConnectionState state, string message)
        {
            if (!session.IsInGame && state == ConnectionState.Connected)
            {
                speechBudgetActive = false;
                speechRound = 0;
                speechUsed = 0;
            }
            RefreshRoom();
            SetMicrophoneMuted(microphoneMuted);
        }
        private void OnCodeChanged(string code) => RefreshRoom();
        private void OnRoleChanged() => SetMicrophoneMuted(microphoneMuted);

        private void RefreshRoom()
        {
            string channel = session.State == ConnectionState.Connected && !string.IsNullOrEmpty(session.JoinCode)
                ? "carryon-room-" + session.JoinCode.Trim().ToUpperInvariant() : "";
            if (channel == desiredChannel) return;
            desiredChannel = channel;
            retries = 0;
            // Stop capture immediately, even if an earlier login/join is still awaiting a response.
            if (service?.InitializationState == VivoxInitializationState.Initialized) service.MuteInputDevice();
            IsConnected = false;
            Reconcile();
        }

        private void Update()
        {
            bool held = ReadTalkKey();
            if (held != PushToTalkHeld) SetMicrophoneMuted(microphoneMuted);
            if (!busy && !IsConnected && desiredChannel.Length > 0 && retries < 3 && Time.unscaledTime >= retryAt)
                Reconcile();
            if (!speechBudgetActive || !session.IsInGame || !IsConnected ||
                !PushToTalkHeld || ListenOnly || microphoneMuted ||
                service == null || service.IsInputDeviceMuted || SpeechExhausted) return;
            if (service.ActiveChannels.TryGetValue(connectedChannel, out var people))
                foreach (var person in people)
                    if (person.IsSelf)
                    {
                        CountSpeech(Time.unscaledDeltaTime, person.SpeechDetected);
                        break;
                    }
        }

        // Called from the replicated round, not from panel enable or voice reconnect.
        public void BeginSpeechRound(uint round, bool isHacker, bool interviewActive = false)
        {
            if (round == 0) return;
            if (speechRound != round) { speechRound = round; speechUsed = 0; }
            speechBudgetActive = isHacker && interviewActive;
            SetMicrophoneMuted(microphoneMuted);
        }

        private void CountSpeech(float seconds, bool speaking)
        {
            if (!PushToTalkHeld || !speechBudgetActive || ListenOnly || microphoneMuted ||
                !speaking || seconds <= 0 || SpeechExhausted) return;
            speechUsed = Mathf.Min(hackerSpeechSeconds, speechUsed + seconds);
            if (SpeechExhausted) SetMicrophoneMuted(microphoneMuted);
        }

        [ContextMenu("Retry Voice Connection")]
        public void RetryConnection()
        {
            retries = 0;
            retryAt = 0;
            if (Application.isPlaying && !stopped) Reconcile();
        }

        // Serialize SDK operations. If the player leaves during login/join, finish that
        // operation muted, then clean up; never let a stale continuation reopen an old room.
        private async void Reconcile()
        {
            if (busy) return;
            busy = true;
            string attempted = desiredChannel;
            try
            {
                while (true)
                {
                    attempted = stopped ? "" : desiredChannel;
                    if (connectedChannel == attempted && (attempted.Length == 0
                        ? service == null || !service.IsLoggedIn : IsConnected))
                    {
                        if (attempted.Length == 0)
                        {
                            ParticipantCount = 0;
                            SetStatus("Voice: offline");
                        }
                        break;
                    }
                    if (service != null && service.IsLoggedIn)
                    {
                        service.MuteInputDevice();
                        await service.LeaveAllChannelsAsync();
                    }
                    connectedChannel = "";
                    IsConnected = false;
                    ParticipantCount = 0;
                    if (attempted.Length == 0)
                    {
                        if (service != null && service.IsLoggedIn) await service.LogoutAsync();
                        SetStatus("Voice: offline");
                        break;
                    }

                    SetStatus("Voice: connecting...");
                    // Room creation/join already initialized UGS and signed in. Reuse that identity.
                    if (UnityServices.State != ServicesInitializationState.Initialized || !AuthenticationService.Instance.IsSignedIn)
                        throw new InvalidOperationException("Join the game room before starting voice.");
                    await PrepareSettingsAsync();
                    Subscribe();
                    service.MuteInputDevice();
                    if (stopped || attempted != desiredChannel) continue;
#if UNITY_STANDALONE_OSX || UNITY_IOS
                    if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                    {
                        var permission = Application.RequestUserAuthorization(UserAuthorization.Microphone);
                        while (!permission.isDone) await Task.Yield();
                        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                            throw new InvalidOperationException("Microphone permission denied. Allow microphone access in system settings.");
                    }
#endif
                    if (stopped || attempted != desiredChannel) continue;
                    if (!service.IsLoggedIn)
                        await service.LoginAsync(new LoginOptions { DisplayName = "Player " + session.LocalClientId, EnableTTS = false });
                    if (stopped || attempted != desiredChannel) continue;
                    joiningChannel = attempted;
                    channelJoined = new TaskCompletionSource<bool>();
                    await service.JoinGroupChannelAsync(attempted, ChatCapability.AudioOnly,
                        new ChannelOptions { MakeActiveChannelUponJoining = true });
                    if (await Task.WhenAny(channelJoined.Task, Task.Delay(30000)) != channelJoined.Task)
                        throw new TimeoutException("Vivox channel join timed out.");
                    await channelJoined.Task;
                    connectedChannel = attempted;
                    joiningChannel = "";
                    if (stopped || attempted != desiredChannel) continue;
                    // Explicitly select the transmit channel; do not rely on SDK defaults.
                    await service.SetChannelTransmissionModeAsync(TransmissionMode.Single, attempted);
                    if (stopped || attempted != desiredChannel) continue;
                    service.UnmuteOutputDevice();
                    IsConnected = true;
                    SetMicrophoneMuted(microphoneMuted);
                    retries = 0;
                    RefreshParticipants();
                    break;
                }
            }
            catch (Exception error)
            {
                IsConnected = false;
                retries++;
                retryAt = Time.unscaledTime + 10;
                if (service?.InitializationState == VivoxInitializationState.Initialized) service.MuteInputDevice();
                SetStatus("Voice: unavailable (" + error.GetType().Name + "). Check Vivox service / microphone.");
                Debug.LogWarning("[Voice] " + error.GetType().Name + ": " + error.Message);
            }
            finally
            {
                busy = false;
                if (stopped) Unsubscribe();
                // A changed target must be processed even when an old operation failed.
                if (attempted != (stopped ? "" : desiredChannel)) Reconcile();
            }
        }

        public void SetMicrophoneMuted(bool muted)
        {
            microphoneMuted = muted;
            // Re-read on reconnect/device changes too, so an old key state cannot open the mic.
            PushToTalkHeld = ReadTalkKey();
            if (service?.InitializationState == VivoxInitializationState.Initialized)
            {
                if (muted || ListenOnly || SpeechExhausted || !IsConnected || stopped || connectedChannel != desiredChannel ||
                    (PushToTalkRequired && !PushToTalkHeld)) service.MuteInputDevice();
                else service.UnmuteInputDevice();
            }
            if (IsConnected) RefreshParticipants();
        }

        private bool ReadTalkKey()
        {
            if (!PushToTalkRequired || !speechBudgetActive || ListenOnly || !Application.isFocused || applicationPaused ||
                Keyboard.current == null || !Keyboard.current.tKey.isPressed) return false;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null) return true;
            var tmp = selected.GetComponentInParent<TMPro.TMP_InputField>();
            var legacy = selected.GetComponentInParent<UnityEngine.UI.InputField>();
            return !(tmp != null && tmp.isFocused) && !(legacy != null && legacy.isFocused);
        }

        private void OnApplicationFocus(bool focused) => SetMicrophoneMuted(microphoneMuted);
        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            SetMicrophoneMuted(microphoneMuted);
        }

        private void Subscribe()
        {
            if (subscribed) return;
            service.ChannelJoined += OnChannelJoined;
            service.ChannelLeft += OnChannelLeft;
            service.LoggedOut += OnLoggedOut;
            service.ParticipantAddedToChannel += OnParticipantChanged;
            service.ParticipantRemovedFromChannel += OnParticipantChanged;
            service.ConnectionRecovering += OnRecovering;
            service.ConnectionRecovered += OnRecovered;
            subscribed = true;
        }

        private void OnChannelJoined(string channel)
        {
            if (channel == joiningChannel) channelJoined?.TrySetResult(true);
        }
        private void OnChannelLeft(string channel)
        {
            if (channel != connectedChannel) return;
            IsConnected = false;
            connectedChannel = "";
            ParticipantCount = 0;
            retryAt = Time.unscaledTime + 10;
            if (!stopped && desiredChannel.Length > 0) SetStatus("Voice: disconnected; reconnecting...");
        }
        private void OnLoggedOut()
        {
            IsConnected = false;
            connectedChannel = "";
            ParticipantCount = 0;
            if (!stopped && desiredChannel.Length > 0) SetStatus("Voice: disconnected; reconnecting...");
        }
        private void OnRecovering()
        {
            IsConnected = false;
            retryAt = Time.unscaledTime + 35; // Let Vivox finish its own recovery first.
            SetStatus("Voice: reconnecting...");
        }
        private void OnRecovered()
        {
            if (stopped || connectedChannel.Length == 0 || connectedChannel != desiredChannel) return;
            IsConnected = true;
            SetMicrophoneMuted(microphoneMuted);
        }
        private void OnParticipantChanged(VivoxParticipant participant)
        {
            ApplyTeammateVolume();
            RefreshParticipants();
        }
        private void RefreshParticipants()
        {
            if (!IsConnected || service == null) return;
            ParticipantCount = service.ActiveChannels.TryGetValue(connectedChannel, out var people) ? people.Count : 0;
            ApplyTeammateVolume();
            SetStatus("Voice: connected (" + ParticipantCount + "/2) - " +
                (PushToTalkRequired && !speechBudgetActive ? "voice locked outside Hacker interview" :
                 ListenOnly ? "listen only (Traveler)" : SpeechExhausted ? "speech time exhausted" :
                 microphoneMuted ? "mic muted" : PushToTalkRequired ?
                 (PushToTalkHeld ? "transmitting (T held)" : "hold T to talk") : "mic on"));
        }

        // Shared by the menu and room connection so opening settings cannot initialize the SDK twice.
        public Task PrepareSettingsAsync()
        {
            if (preparation == null || preparation.IsFaulted || preparation.IsCanceled)
                preparation = PrepareAsync();
            return preparation;
        }

        private async Task PrepareAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            service ??= VivoxService.Instance;
            if (service.InitializationState != VivoxInitializationState.Initialized)
                await service.InitializeAsync();
            service.SetInputDeviceVolume(InputVolume);
            service.SetOutputDeviceVolume(OutputVolume);
            SetMicrophoneMuted(microphoneMuted);
        }

        public void SetInputVolume(float value)
        {
            InputVolume = Mathf.Clamp(Mathf.RoundToInt(value), -50, 50);
            if (service?.InitializationState == VivoxInitializationState.Initialized)
                service.SetInputDeviceVolume(InputVolume);
        }

        public void SetOutputVolume(float value)
        {
            OutputVolume = Mathf.Clamp(Mathf.RoundToInt(value), -50, 50);
            if (service?.InitializationState == VivoxInitializationState.Initialized)
                service.SetOutputDeviceVolume(OutputVolume);
        }

        public void SetTeammateVolume(float value)
        {
            TeammateVolume = Mathf.Clamp(Mathf.RoundToInt(value), -50, 50);
            ApplyTeammateVolume();
        }

        private void ApplyTeammateVolume()
        {
            if (service == null || !service.IsLoggedIn) return;
            foreach (var channel in service.ActiveChannels.Values)
                foreach (var person in channel)
                    if (!person.IsSelf) person.SetLocalVolume(TeammateVolume);
        }
        private void SetStatus(string value)
        {
            if (Status == value) return;
            Status = value;
            if (!stopped) Changed?.Invoke();
            Debug.Log("[Voice] " + value);
        }

        private void OnDisable()
        {
            stopped = true;
            session.StateChanged -= OnSessionChanged;
            session.JoinCodeChanged -= OnCodeChanged;
            session.LobbyChanged -= OnRoleChanged;
            desiredChannel = "";
            IsConnected = false;
            if (service?.InitializationState == VivoxInitializationState.Initialized) service.MuteInputDevice();
            Reconcile();
        }
        private void Unsubscribe()
        {
            if (!subscribed || service == null) return;
            service.ChannelJoined -= OnChannelJoined;
            service.ChannelLeft -= OnChannelLeft;
            service.LoggedOut -= OnLoggedOut;
            service.ParticipantAddedToChannel -= OnParticipantChanged;
            service.ParticipantRemovedFromChannel -= OnParticipantChanged;
            service.ConnectionRecovering -= OnRecovering;
            service.ConnectionRecovered -= OnRecovered;
            subscribed = false;
        }
    }
}
