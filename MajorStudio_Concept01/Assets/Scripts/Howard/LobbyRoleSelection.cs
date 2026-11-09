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
        private readonly NetworkVariable<LobbyRoleState> state = new(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly Dictionary<ulong, ulong> lastRequestIds = new();
        private NetworkSessionManager session;
        private ulong nextRequestId, pendingRequestId;
        private float pendingSince;

        public event Action Changed;
        public LobbyRoleState Current => state.Value;
        public bool HasPendingRequest => pendingRequestId != 0;
        public string Feedback { get; private set; } = "Choose a role, then Ready. Release your role to swap.";
        public bool CanStartGame => IsSpawned && !state.Value.GameStarted && state.Value.BothReady &&
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
                        Role = LobbyRole.Traveler, Version = 1
                    }
                };
                foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                    if (clientId != initial.Host.ClientId)
                        initial.Client = NewClient(clientId, initial.Host.Role);
                state.Value = initial;
            }
            session?.RegisterRoleSelection(this);
            Changed?.Invoke();
        }

        private static LobbyPlayerSelection NewClient(ulong id, LobbyRole hostRole) => new()
        {
            ClientId = id, Connected = true, Version = 1,
            Role = hostRole == LobbyRole.Hacker ? LobbyRole.Traveler : LobbyRole.Hacker
        };

        private void OnClientConnected(ulong id)
        {
            if (!IsServer || id == state.Value.Host.ClientId || state.Value.TryGetPlayer(id, out _)) return;
            var current = state.Value;
            current.Client = NewClient(id, current.Host.Role);
            current.Host.Ready = false;
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
                current.Host.Ready = false;
                current.Host.Version++;
            }
            else if (current.Host.Connected && id == current.Host.ClientId)
                current = default;
            else return;
            lastRequestIds.Remove(id);
            state.Value = current;
        }

        public bool TryGetLocalPlayer(out LobbyPlayerSelection player) =>
            state.Value.TryGetPlayer(NetworkManager.LocalClientId, out player);

        public bool TryGetRole(ulong id, out PlayerRole role) => state.Value.TryGetRole(id, out role);

        public void SelectRole(LobbyRole role) => Submit(false, role, false);
        public void ReleaseRole() => SelectRole(LobbyRole.Unassigned);
        public void SetReady(bool ready) => Submit(true, default, ready);
        public void ToggleReady()
        {
            if (TryGetLocalPlayer(out var player)) SetReady(!player.Ready);
        }

        private void Submit(bool readyRequest, LobbyRole role, bool ready)
        {
            if (!IsSpawned || HasPendingRequest || !TryGetLocalPlayer(out var player)) return;
            pendingRequestId = ++nextRequestId;
            pendingSince = Time.unscaledTime;
            Feedback = "Waiting for Host confirmation...";
            Changed?.Invoke();
            SelectRoleRpc(pendingRequestId, player.Version, readyRequest, role, ready);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SelectRoleRpc(ulong requestId, uint expectedVersion, bool readyRequest,
            LobbyRole role, bool ready, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            string error = Validate(sender, requestId, expectedVersion, readyRequest, role, ready);
            if (string.IsNullOrEmpty(error))
            {
                var current = state.Value;
                current.TryGetPlayer(sender, out var player);
                if (readyRequest) player.Ready = ready;
                else { player.Role = role; player.Ready = false; }
                player.Version++;
                if (sender == current.Host.ClientId) current.Host = player;
                else current.Client = player;
                state.Value = current;
            }
            SelectionResultRpc(sender, requestId, new FixedString128Bytes(error ?? string.Empty));
        }

        private string Validate(ulong sender, ulong requestId, uint version, bool readyRequest, LobbyRole role, bool ready)
        {
            if (!IsServer || !NetworkManager.ConnectedClients.ContainsKey(sender) ||
                !state.Value.TryGetPlayer(sender, out var player)) return "Player is not in this Lobby.";
            if (requestId == 0 || (lastRequestIds.TryGetValue(sender, out ulong previous) && requestId <= previous))
                return "This request has already been processed.";
            lastRequestIds[sender] = requestId;
            if (state.Value.GameStarted || (session != null && session.IsInGame)) return "Roles are locked during the game.";
            if (version != player.Version) return "Selection changed. Please try again.";
            if (readyRequest)
                return ready && player.Role == LobbyRole.Unassigned ? "Select a role before becoming Ready." : string.Empty;
            if (player.Ready) return "Cancel Ready before changing or releasing your role.";
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

        internal bool TryLockForStart()
        {
            if (!IsServer || !CanStartGame) return false;
            var current = state.Value;
            current.GameStarted = true;
            state.Value = current;
            return true;
        }

        internal void UnlockAfterFailedStart()
        {
            if (!IsServer) return;
            var current = state.Value;
            current.GameStarted = false;
            state.Value = current;
        }

        internal void ReturnToLobby()
        {
            if (!IsServer) return;
            var current = state.Value;
            current.GameStarted = false;
            current.Host.Ready = false;
            current.Client.Ready = false;
            current.Host.Version++;
            current.Client.Version++;
            state.Value = current;
        }

        private void OnStateChanged(LobbyRoleState previous, LobbyRoleState current) => Changed?.Invoke();

        private void Update()
        {
            if (!HasPendingRequest || Time.unscaledTime - pendingSince < 10) return;
            pendingRequestId = 0;
            Feedback = "Request timed out. Please try again.";
            Changed?.Invoke();
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
