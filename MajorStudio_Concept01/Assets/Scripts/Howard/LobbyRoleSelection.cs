using System;
using System.Collections.Generic;
using CarryOn.Taylor;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Game.Multiplayer
{
    [DisallowMultipleComponent, RequireComponent(typeof(NetworkObject))]
    public sealed class LobbyRoleSelection : NetworkBehaviour
    {
        [SerializeField] private CutsceneCarousel cutscenePrefab;
        [SerializeField, Min(0.1f)] private float countdownDuration = 10;
        private CutsceneCarousel presentation;
        private readonly NetworkVariable<LobbyRoleState> state = new(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly Dictionary<ulong, ulong> lastRequestIds = new();
        private NetworkSessionManager session;
        private ulong nextRequestId, pendingRequestId;
        private float pendingSince;

        public event Action Changed;
        public LobbyRoleState Current => state.Value;
        public bool HasPendingRequest => pendingRequestId != 0;
        public string Feedback { get; private set; } = "Select Person or Briefcase. Click your selection again to deselect.";
        public double ServerNow => NetworkManager.ServerTime.Time;
        public bool CanStartGame => IsSpawned && state.Value.Stage == SessionStage.Lobby &&
            NetworkManager.ConnectedClientsIds.Count == NetworkSessionManager.MaximumPlayers;

        public override void OnNetworkSpawn()
        {
            DontDestroyOnLoad(gameObject);
            session = NetworkManager.GetComponent<NetworkSessionManager>();
            state.OnValueChanged += OnStateChanged;
            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                var initial = new LobbyRoleState
                {
                    Host = new LobbyPlayerSelection
                    {
                        ClientId = Unity.Netcode.NetworkManager.ServerClientId, Connected = true,
                        Role = LobbyRole.Unassigned, Version = 1
                    }
                };
                foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                    if (clientId != initial.Host.ClientId)
                        initial.Client = NewClient(clientId);
                state.Value = initial;
            }
            session?.RegisterRoleSelection(this);
            if (cutscenePrefab != null)
            {
                presentation = Instantiate(cutscenePrefab, transform, false);
                presentation.Bind(this);
            }
            Changed?.Invoke();
        }

        private static LobbyPlayerSelection NewClient(ulong id) => new()
        {
            ClientId = id, Connected = true, Version = 1,
            Role = LobbyRole.Unassigned
        };

        private void OnClientConnected(ulong id)
        {
            if (!IsServer || id == state.Value.Host.ClientId || state.Value.TryGetPlayer(id, out _)) return;
            var current = state.Value;
            current.Client = NewClient(id);
            current.Host.Version++;
            state.Value = current;
        }

        private void OnClientDisconnected(ulong id)
        {
            if (!IsServer) return;
            var current = state.Value;
            if (current.Client.Connected && id == current.Client.ClientId)
            {
                current.Client = default;
                current.Host.Version++;
            }
            else if (current.Host.Connected && id == current.Host.ClientId)
                current = default;
            else return;
            lastRequestIds.Remove(id);
            CancelCountdown(ref current);
            // A pre-game departure returns the survivor to the custom Lobby. A new pair replays the comics.
            if (!current.GameStarted)
            {
                current.Stage = SessionStage.Lobby;
                current.ComicSequence++;
                current.Host.Role = LobbyRole.Unassigned;
            }
            state.Value = current;
        }

        public bool TryGetLocalPlayer(out LobbyPlayerSelection player) =>
            state.Value.TryGetPlayer(NetworkManager.LocalClientId, out player);

        public bool TryGetRole(ulong id, out PlayerRole role) => state.Value.TryGetRole(id, out role);

        public void SelectRole(LobbyRole role) => Submit(role);
        public void ReleaseRole() => SelectRole(LobbyRole.Unassigned);

        private void Submit(LobbyRole role)
        {
            if (!IsSpawned || HasPendingRequest || !TryGetLocalPlayer(out var player)) return;
            pendingRequestId = ++nextRequestId;
            pendingSince = Time.unscaledTime;
            Feedback = "Waiting for Host confirmation...";
            Changed?.Invoke();
            SelectRoleRpc(pendingRequestId, player.Version, role);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SelectRoleRpc(ulong requestId, uint expectedVersion, LobbyRole role, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            string error = Validate(sender, requestId, expectedVersion, role);
            if (string.IsNullOrEmpty(error))
            {
                var current = state.Value;
                current.TryGetPlayer(sender, out var player);
                bool changed = player.Role != role;
                player.Role = role;
                player.Version++;
                if (sender == current.Host.ClientId) current.Host = player;
                else current.Client = player;
                if (changed) CancelCountdown(ref current);
                state.Value = current;
            }
            SelectionResultRpc(sender, requestId, new FixedString128Bytes(error ?? string.Empty));
        }

        private string Validate(ulong sender, ulong requestId, uint version, LobbyRole role)
        {
            if (!IsServer || !NetworkManager.ConnectedClients.ContainsKey(sender) ||
                !state.Value.TryGetPlayer(sender, out var player)) return "Player is not in this Lobby.";
            if (requestId == 0 || (lastRequestIds.TryGetValue(sender, out ulong previous) && requestId <= previous))
                return "This request has already been processed.";
            lastRequestIds[sender] = requestId;
            if (state.Value.GameStarted || (session != null && session.IsInGame)) return "Roles are locked during the game.";
            if (state.Value.Stage != SessionStage.CharacterSelection) return "Character selection is not open yet.";
            if (version != player.Version) return "Selection changed. Please try again.";
            if (!Enum.IsDefined(typeof(LobbyRole), role)) return "Invalid role.";
            if (state.Value.IsOccupied(role, sender)) return "That role is occupied. Its player must release it first.";
            return string.Empty;
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void SelectionResultRpc(ulong clientId, ulong requestId, FixedString128Bytes error)
        {
            if (NetworkManager.LocalClientId != clientId || pendingRequestId != requestId) return;
            pendingRequestId = 0;
            Feedback = error.Length == 0 ? "Selection confirmed by Host." : error.ToString();
            Changed?.Invoke();
        }

        internal bool BeginCutscene()
        {
            if (!IsServer || !CanStartGame || presentation == null || presentation.PanelCount == 0) return false;
            var current = state.Value;
            current.Host.Role = current.Client.Role = LobbyRole.Unassigned;
            current.Host.Version++;
            current.Client.Version++;
            CancelCountdown(ref current);
            EnterComic(ref current, 0);
            state.Value = current;
            return true;
        }

        private void EnterComic(ref LobbyRoleState current, int index)
        {
            current.Stage = SessionStage.ComicEntering;
            current.ComicIndex = index;
            current.NarrationSegmentIndex = -1;
            current.ComicSequence++;
            current.StageStartedAt = ServerNow;
            current.StageEndsAt = ServerNow + presentation.TransitionDuration;
        }

        public void NotifyNarrationCompleted(int comicIndex, uint sequence)
        {
            // No client completion RPC: only the Host's final segment deadline can finish narration.
            if (!IsSpawned || !IsServer || presentation == null) return;
            var current = state.Value;
            if (current.Stage != SessionStage.ComicNarration || current.ComicIndex != comicIndex ||
                current.ComicSequence != sequence || ServerNow < current.StageEndsAt ||
                current.NarrationSegmentIndex + 1 < presentation.SegmentCount(comicIndex)) return;
            current.Stage = SessionStage.ComicHold;
            current.StageStartedAt = ServerNow;
            current.StageEndsAt = ServerNow + presentation.HoldDuration(comicIndex);
            state.Value = current;
        }

        private static void CancelCountdown(ref LobbyRoleState current)
        {
            current.CountdownActive = false;
            current.CountdownEndsAt = 0;
            current.CountdownGeneration++;
        }

        internal bool TryLockForStart(uint generation)
        {
            if (!IsServer || state.Value.GameStarted || state.Value.Stage != SessionStage.CharacterSelection ||
                !state.Value.CountdownActive || state.Value.CountdownGeneration != generation ||
                ServerNow < state.Value.CountdownEndsAt || !state.Value.HasDistinctRoles ||
                NetworkManager.ConnectedClientsIds.Count != NetworkSessionManager.MaximumPlayers) return false;
            var current = state.Value;
            current.GameStarted = true;
            current.Stage = SessionStage.Gameplay;
            CancelCountdown(ref current);
            state.Value = current;
            return true;
        }

        internal void UnlockAfterFailedStart()
        {
            if (!IsServer) return;
            var current = state.Value;
            current.GameStarted = false;
            current.Stage = SessionStage.CharacterSelection;
            CancelCountdown(ref current);
            state.Value = current;
        }

        internal void ReturnToLobby()
        {
            if (!IsServer) return;
            var current = state.Value;
            current.GameStarted = false;
            current.Stage = SessionStage.Lobby;
            current.ComicSequence++;
            current.Host.Role = current.Client.Role = LobbyRole.Unassigned;
            CancelCountdown(ref current);
            current.Host.Version++;
            current.Client.Version++;
            state.Value = current;
        }

        private void OnStateChanged(LobbyRoleState previous, LobbyRoleState current) => Changed?.Invoke();

        private void Update()
        {
            if (IsSpawned && IsServer) AdvanceSequence();
            if (!HasPendingRequest || Time.unscaledTime - pendingSince < 10) return;
            pendingRequestId = 0;
            Feedback = "Request timed out. Please try again.";
            Changed?.Invoke();
        }

        private void AdvanceSequence()
        {
            var current = state.Value;
            if (NetworkManager.ConnectedClientsIds.Count != NetworkSessionManager.MaximumPlayers) return;
            if (current.Stage == SessionStage.ComicEntering && ServerNow >= current.StageEndsAt)
            {
                current.Stage = SessionStage.ComicNarration;
                current.NarrationSegmentIndex = 0;
                current.StageStartedAt = ServerNow;
                current.StageEndsAt = ServerNow + presentation.SegmentDuration(current.ComicIndex, 0);
                state.Value = current;
            }
            else if (current.Stage == SessionStage.ComicNarration && ServerNow >= current.StageEndsAt)
            {
                if (current.NarrationSegmentIndex + 1 < presentation.SegmentCount(current.ComicIndex))
                {
                    current.NarrationSegmentIndex++;
                    current.StageStartedAt = ServerNow;
                    current.StageEndsAt = ServerNow + presentation.SegmentDuration(current.ComicIndex, current.NarrationSegmentIndex);
                    state.Value = current;
                }
                else NotifyNarrationCompleted(current.ComicIndex, current.ComicSequence);
            }
            else if (current.Stage == SessionStage.ComicHold && ServerNow >= current.StageEndsAt)
            {
                if (current.ComicIndex + 1 < presentation.PanelCount) EnterComic(ref current, current.ComicIndex + 1);
                else
                {
                    current.Stage = SessionStage.SelectionEntering;
                    current.StageStartedAt = ServerNow;
                    current.StageEndsAt = ServerNow + presentation.SelectionScaleDuration;
                }
                state.Value = current;
            }
            else if (current.Stage == SessionStage.SelectionEntering && ServerNow >= current.StageEndsAt)
            {
                current.Stage = SessionStage.CharacterSelection;
                state.Value = current;
            }
            else if (current.Stage == SessionStage.CharacterSelection && current.HasDistinctRoles)
            {
                if (!current.CountdownActive)
                {
                    current.CountdownGeneration++;
                    current.CountdownActive = true;
                    current.CountdownEndsAt = ServerNow + Mathf.Max(0.1f, countdownDuration);
                    state.Value = current;
                }
                else if (ServerNow >= current.CountdownEndsAt)
                    session?.LoadSelectedGameplay(current.CountdownGeneration);
            }
        }

        public override void OnNetworkDespawn() => Unsubscribe();

        private void Unsubscribe()
        {
            state.OnValueChanged -= OnStateChanged;
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            pendingRequestId = 0;
            if (presentation != null) Destroy(presentation.gameObject);
            presentation = null;
            if (session != null) session.UnregisterRoleSelection(this);
            session = null;
        }

        public override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
        }
    }
}
