using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Multiplayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class NetworkSessionManager : MonoBehaviour
    {
        public const int MaximumPlayers = 2;
        public const string GameplaySceneName = "GameScene";
        private const int JoiningClientCapacity = MaximumPlayers - 1;
        private const int ConnectionTimeoutSeconds = 20;
        private const string RelayConnectionType = "dtls";

        private readonly List<NetworkPlayerInfo> players = new(MaximumPlayers);
        private NetworkManager networkManager;
        private UnityTransport transport;
        private CancellationTokenSource operationCancellation;
        private TaskCompletionSource<bool> connectionCompletion;
        private int operationVersion;
        private NetworkSceneManager subscribedSceneManager;

        public event Action<ConnectionState, string> StateChanged;
        public event Action<IReadOnlyList<NetworkPlayerInfo>> PlayersChanged;
        public event Action<string> JoinCodeChanged;

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public string StatusMessage { get; private set; } = "Disconnected";
        public string JoinCode { get; private set; } = string.Empty;
        public bool IsBusy => State is ConnectionState.Initializing or ConnectionState.CreatingRoom
            or ConnectionState.JoiningRoom or ConnectionState.Connecting or ConnectionState.Disconnecting;
        public bool IsHost => networkManager != null && networkManager.IsHost;
        public ulong LocalClientId => networkManager != null && networkManager.IsListening
            ? networkManager.LocalClientId
            : 0;
        public IReadOnlyList<NetworkPlayerInfo> Players => players;
        public bool IsInGame { get; private set; }
        public bool CanStartGame => IsHost && State == ConnectionState.Connected && !IsInGame &&
            networkManager.ConnectedClientsIds.Count == MaximumPlayers;

        private void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.NetworkConfig.PlayerPrefab = null;
            networkManager.ConnectionApprovalCallback = ApproveConnection;
            SubscribeToNetworkEvents();
            RefreshPlayers();
        }

        private void OnDestroy()
        {
            CancelPendingOperation();
            UnsubscribeFromNetworkEvents();
            if (networkManager != null && networkManager.ConnectionApprovalCallback == ApproveConnection)
                networkManager.ConnectionApprovalCallback = null;
        }

        public void StartGame()
        {
            if (!CanStartGame) return;
            IsInGame = true;
            var result = networkManager.SceneManager.LoadScene(GameplaySceneName, LoadSceneMode.Additive);
            if (result != SceneEventProgressStatus.Started)
            {
                IsInGame = false;
                SetState(ConnectionState.Connected, $"Could not load GameScene: {result}");
                return;
            }
            SetState(ConnectionState.Connected, "Loading GameScene...");
        }

        [ContextMenu("Return To Lobby (Host)")]
        public void ReturnToLobby()
        {
            if (!IsHost || !IsInGame) return;
            Scene scene = SceneManager.GetSceneByName(GameplaySceneName);
            if (scene.IsValid() && scene.isLoaded)
                networkManager.SceneManager.UnloadScene(scene);
        }

        private void SubscribeToSceneEvents()
        {
            if (subscribedSceneManager == networkManager.SceneManager) return;
            if (subscribedSceneManager != null) subscribedSceneManager.OnSceneEvent -= OnNetworkSceneEvent;
            subscribedSceneManager = networkManager.SceneManager;
            if (subscribedSceneManager != null) subscribedSceneManager.OnSceneEvent += OnNetworkSceneEvent;
        }

        private void OnNetworkSceneEvent(SceneEvent sceneEvent)
        {
            if (sceneEvent.SceneName != GameplaySceneName) return;
            if (sceneEvent.SceneEventType == SceneEventType.Load)
            {
                IsInGame = true;
                SetState(ConnectionState.Connected, "Loading GameScene...");
            }
            else if (sceneEvent.SceneEventType == SceneEventType.UnloadEventCompleted)
            {
                IsInGame = false;
                SetState(ConnectionState.Connected, "Returned to Lobby");
            }
        }

        private void LateUpdate()
        {
            // Additive GameScene leaves the original lobby and its one NetworkManager intact.
            if (!IsInGame || networkManager.IsListening || networkManager.ShutdownInProgress) return;
            IsInGame = false;
            Scene scene = SceneManager.GetSceneByName(GameplaySceneName);
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            StateChanged?.Invoke(State, StatusMessage);
        }

        public async void CreateRoom()
        {
            if (IsBusy || (networkManager != null && networkManager.IsListening))
                return;

            int version = BeginOperation();
            try
            {
                SetState(ConnectionState.Initializing, "Initializing Services");
                await InitializeServicesAsync();
                ThrowIfOperationWasCancelled(version);

                SetState(ConnectionState.CreatingRoom, "Creating Room");
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(JoiningClientCapacity);
                ThrowIfOperationWasCancelled(version);

                string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                ThrowIfOperationWasCancelled(version);

                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, RelayConnectionType));
                if (!networkManager.StartHost())
                    throw new InvalidOperationException("Netcode could not start the Host.");
                SubscribeToSceneEvents();

                JoinCode = code;
                JoinCodeChanged?.Invoke(JoinCode);
                RefreshPlayers();
                SetState(ConnectionState.Connected, "Room created. Waiting for Player 2...");
            }
            catch (OperationCanceledException)
            {
                // LeaveRoom owns the final state when the user cancels an operation.
            }
            catch (Exception exception)
            {
                FailOperation("Connection Failed", exception);
            }
        }

        public async void JoinRoom(string rawJoinCode)
        {
            if (IsBusy || (networkManager != null && networkManager.IsListening))
                return;

            string normalizedCode = (rawJoinCode ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                SetState(ConnectionState.Error, "Invalid Join Code: enter a code first.");
                return;
            }

            int version = BeginOperation();
            try
            {
                SetState(ConnectionState.Initializing, "Initializing Services");
                await InitializeServicesAsync();
                ThrowIfOperationWasCancelled(version);

                SetState(ConnectionState.JoiningRoom, "Joining Room");
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(normalizedCode);
                ThrowIfOperationWasCancelled(version);

                transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, RelayConnectionType));
                connectionCompletion = new TaskCompletionSource<bool>();
                SetState(ConnectionState.Connecting, "Connecting");
                if (!networkManager.StartClient())
                    throw new InvalidOperationException("Netcode could not start the Client.");
                SubscribeToSceneEvents();

                Task timeout = Task.Delay(TimeSpan.FromSeconds(ConnectionTimeoutSeconds), operationCancellation.Token);
                Task completed = await Task.WhenAny(connectionCompletion.Task, timeout);
                ThrowIfOperationWasCancelled(version);

                if (completed != connectionCompletion.Task)
                    throw new TimeoutException("The connection attempt timed out.");
                if (!connectionCompletion.Task.Result)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(networkManager.DisconnectReason)
                        ? "The server rejected the connection."
                        : networkManager.DisconnectReason);

                JoinCode = normalizedCode;
                JoinCodeChanged?.Invoke(JoinCode);
                RefreshPlayers();
                SetState(ConnectionState.Connected, "Connected");
            }
            catch (OperationCanceledException)
            {
                // LeaveRoom owns the final state when the user cancels an operation.
            }
            catch (Exception exception)
            {
                FailOperation(MapJoinFailure(exception), exception);
            }
        }

        [ContextMenu("Leave Room")]
        public void LeaveRoom()
        {
            if (State == ConnectionState.Disconnecting)
                return;

            operationVersion++;
            CancelPendingOperation();
            SetState(ConnectionState.Disconnecting, "Disconnecting");

            if (networkManager != null && networkManager.IsListening)
                networkManager.Shutdown();

            connectionCompletion = null;
            JoinCode = string.Empty;
            JoinCodeChanged?.Invoke(JoinCode);
            RefreshPlayers();
            SetState(ConnectionState.Disconnected, "Disconnected");
        }

        private static async Task InitializeServicesAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private int BeginOperation()
        {
            CancelPendingOperation();
            operationCancellation = new CancellationTokenSource();
            return ++operationVersion;
        }

        private void ThrowIfOperationWasCancelled(int version)
        {
            if (version != operationVersion || operationCancellation == null || operationCancellation.IsCancellationRequested)
                throw new OperationCanceledException();
        }

        private void CancelPendingOperation()
        {
            if (operationCancellation == null)
                return;

            operationCancellation.Cancel();
            operationCancellation.Dispose();
            operationCancellation = null;
        }

        private void FailOperation(string userMessage, Exception exception)
        {
            Debug.LogError($"Multiplayer session error: {exception}");
            if (networkManager != null && networkManager.IsListening)
                networkManager.Shutdown(true);
            connectionCompletion = null;
            JoinCode = string.Empty;
            JoinCodeChanged?.Invoke(JoinCode);
            RefreshPlayers();
            SetState(ConnectionState.Error, userMessage);
        }

        private static string MapJoinFailure(Exception exception)
        {
            string details = exception.Message.ToLowerInvariant();
            if (details.Contains("full") || details.Contains("maximum") || details.Contains("409"))
                return "Room Full";
            if (details.Contains("join code") || details.Contains("not found") || details.Contains("404")
                || details.Contains("invalid"))
                return "Invalid Join Code";
            if (exception is TimeoutException)
                return "Connection Failed: timed out.";
            return "Connection Failed";
        }

        private void SubscribeToNetworkEvents()
        {
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            networkManager.OnTransportFailure += OnTransportFailure;
        }

        private void UnsubscribeFromNetworkEvents()
        {
            if (subscribedSceneManager != null) subscribedSceneManager.OnSceneEvent -= OnNetworkSceneEvent;
            subscribedSceneManager = null;
            if (networkManager == null)
                return;
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            networkManager.OnTransportFailure -= OnTransportFailure;
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            bool isLocalHost = request.ClientNetworkId == NetworkManager.ServerClientId;
            bool roomHasSpace = networkManager.ConnectedClientsIds.Count < MaximumPlayers;
            response.Approved = isLocalHost || (roomHasSpace && !IsInGame);
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = response.Approved ? string.Empty : IsInGame ? "Game already in progress" : "Room Full";
        }

        private void OnClientConnected(ulong clientId)
        {
            RefreshPlayers();
            if (!networkManager.IsHost && clientId == networkManager.LocalClientId)
                connectionCompletion?.TrySetResult(true);

            if (State == ConnectionState.Connected)
                SetState(ConnectionState.Connected,
                    networkManager.IsHost && players.Count < MaximumPlayers
                        ? "Waiting for Player 2..."
                        : "Connected");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            RefreshPlayers();

            if (State == ConnectionState.Disconnecting || State == ConnectionState.Disconnected)
                return;

            if (!networkManager.IsHost && clientId == networkManager.LocalClientId)
            {
                connectionCompletion?.TrySetResult(false);
                string reason = networkManager.DisconnectReason;
                bool roomFull = reason.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0;
                SetState(ConnectionState.Error, roomFull ? "Room Full" : "Disconnected: Host closed the session.");
                JoinCode = string.Empty;
                JoinCodeChanged?.Invoke(JoinCode);
                return;
            }

            if (networkManager.IsHost)
                SetState(ConnectionState.Connected, "Player 2 disconnected. Waiting for Player 2...");
        }

        private void OnTransportFailure()
        {
            connectionCompletion?.TrySetResult(false);
            if (State is not ConnectionState.Disconnecting and not ConnectionState.Disconnected)
                SetState(ConnectionState.Error, "Connection Failed");
        }

        private void RefreshPlayers()
        {
            players.Clear();
            if (networkManager != null && networkManager.IsHost)
            {
                players.AddRange(networkManager.ConnectedClientsIds
                    .OrderBy(id => id)
                    .Select(id => new NetworkPlayerInfo(id, id == NetworkManager.ServerClientId, true)));
            }
            else if (networkManager != null && networkManager.IsConnectedClient)
            {
                players.Add(new NetworkPlayerInfo(NetworkManager.ServerClientId, true, true));
                if (networkManager.LocalClientId != NetworkManager.ServerClientId)
                    players.Add(new NetworkPlayerInfo(networkManager.LocalClientId, false, true));
            }

            PlayersChanged?.Invoke(players);
        }

        private void SetState(ConnectionState newState, string message)
        {
            State = newState;
            StatusMessage = message;
            StateChanged?.Invoke(State, StatusMessage);
        }
    }
}
