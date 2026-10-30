#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Threading.Tasks;
using CarryOn.Taylor;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

namespace Game.Multiplayer.Editor
{
    public static class LobbyRoleSelectionChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static bool running;
        private static int checks;

        [MenuItem("Tools/Role Selection/Run Loopback Checks (Play Mode)")]
        public static async void Run()
        {
            var host = NetworkManager.Singleton;
            if (!EditorApplication.isPlaying || running || host == null || host.IsListening)
            {
                Debug.LogWarning("Enter Play Mode in an idle MultiplayerTest lobby to run role checks.");
                return;
            }
            running = true;
            checks = 0;
            var rolePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyRoleSelectionSetup.RolePrefabPath);
            var gamePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CoopGameSceneSetup.BridgePrefabPath);
            var hostSession = host.GetComponent<NetworkSessionManager>();
            NetworkManager client = null;
            CoopGameNetworkBridge game = null;
            bool originalScenes = host.NetworkConfig.EnableSceneManagement;
            bool originalSame = host.NetworkConfig.ForceSamePrefabs;
            var transport = host.GetComponent<UnityTransport>();
            var protocolField = typeof(UnityTransport).GetField("m_ProtocolType", Private);
            object originalProtocol = protocolField.GetValue(transport);
            try
            {
                host.NetworkConfig.EnableSceneManagement = false;
                host.NetworkConfig.ForceSamePrefabs = false;
                protocolField.SetValue(transport, UnityTransport.ProtocolType.UnityTransport);
                transport.SetConnectionData("127.0.0.1", 17984);
                Check(host.StartHost(), "Host transport started");
                Call(hostSession, "SpawnRoleSelection");
                Call(hostSession, "SetState", ConnectionState.Connected, "Role check fixture");
                var hostRoles = hostSession.RoleSelection;
                Check(hostRoles.Current.Host.Role == LobbyRole.Traveler && !hostSession.CanStartGame, "Host starts Traveler; one player cannot start");
                var clientObject = new GameObject("RoleCheckClient");
                clientObject.SetActive(false);
                var clientTransport = clientObject.AddComponent<UnityTransport>();
                client = clientObject.AddComponent<NetworkManager>();
                client.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = clientTransport, EnableSceneManagement = false,
                    ConnectionApproval = true, ForceSamePrefabs = false
                };
                var clientSession = clientObject.AddComponent<NetworkSessionManager>();
                clientObject.SetActive(true);
                clientTransport.SetConnectionData("127.0.0.1", 17984);
                if (!client.NetworkConfig.Prefabs.Contains(rolePrefab)) client.AddNetworkPrefab(rolePrefab);
                if (!client.NetworkConfig.Prefabs.Contains(gamePrefab)) client.AddNetworkPrefab(gamePrefab);
                Check(client.StartClient(), "Client transport started");
                await Until(() => client.IsConnectedClient && clientSession.RoleSelection != null &&
                    clientSession.RoleSelection.Current.Client.Connected, "Client joined and role state synchronized");
                var clientRoles = clientSession.RoleSelection;
                await Until(() => hostRoles.Current.Equals(clientRoles.Current), "Identical assignments on both peers");
                Check(clientRoles.Current.Client.Role == LobbyRole.Hacker, "Joining Client defaults to Hacker");
                hostRoles.SelectRole(LobbyRole.Hacker);
                await Confirm(hostRoles);
                Check(hostRoles.Current.Host.Role == LobbyRole.Traveler && hostRoles.Feedback.Contains("occupied"), "Occupied role rejected by Host");
                hostRoles.SetReady(true);
                await Confirm(hostRoles);
                Check(!hostSession.CanStartGame, "One Ready player cannot start");
                hostSession.StartGame();
                Check(!hostSession.IsInGame && !hostRoles.Current.GameStarted, "Host validates readiness again at Start Game");
                hostRoles.ReleaseRole();
                await Confirm(hostRoles);
                Check(hostRoles.Current.Host.Role == LobbyRole.Traveler && hostRoles.Current.Host.Ready, "Ready player cannot release or change role");
                hostRoles.SetReady(false);
                await Confirm(hostRoles);
                hostRoles.ReleaseRole();
                await Confirm(hostRoles);
                hostRoles.SetReady(true);
                await Confirm(hostRoles);
                Check(!hostRoles.Current.Host.Ready && hostRoles.Current.Host.Role == LobbyRole.Unassigned, "Unassigned player cannot Ready");
                clientRoles.ReleaseRole();
                await Confirm(clientRoles);
                await Until(() => !hostRoles.Current.Client.Ready && hostRoles.Current.Client.Role == LobbyRole.Unassigned, "Client role released");
                hostRoles.SelectRole(LobbyRole.Hacker);
                clientRoles.SelectRole(LobbyRole.Hacker);
                await Confirm(clientRoles);
                Check(hostRoles.Current.Host.Role == LobbyRole.Hacker && hostRoles.Current.Client.Role == LobbyRole.Unassigned,
                    "Competing requests cannot give two players the same role");
                clientRoles.SelectRole(LobbyRole.Traveler);
                await Confirm(clientRoles);
                await Until(() => hostRoles.Current.Client.Role == LobbyRole.Traveler, "Both players swapped roles through release and selection");
                Check(!hostRoles.Current.Host.Ready && !hostRoles.Current.Client.Ready, "Role selection resets Ready");
                clientRoles.SelectRole((LobbyRole)255);
                await Confirm(clientRoles);
                Check(hostRoles.Current.Client.Role == LobbyRole.Traveler, "Invalid role enum rejected");
                hostRoles.SetReady(true);
                clientRoles.SetReady(true);
                await Confirm(clientRoles);
                await Until(() => hostSession.CanStartGame, "Both Ready enables Host Start Game");
                Check(!clientSession.CanStartGame, "Client cannot start the game");
                clientSession.StartGame();
                Check(!clientSession.IsInGame && !hostRoles.Current.GameStarted, "Client Start Game call rejected");
                Check(hostRoles.TryLockForStart() && !hostRoles.TryLockForStart(), "Start lock prevents duplicate transitions");
                clientRoles.SetReady(false);
                await Confirm(clientRoles);
                Check(hostRoles.Current.Client.Ready, "Ready/role changes rejected after start lock");
                hostRoles.ReturnToLobby();
                await Until(() => !clientRoles.Current.GameStarted && !clientRoles.Current.Client.Ready, "Return to Lobby resets both Ready states");
                Check(hostRoles.Current.Host.Role == LobbyRole.Hacker && hostRoles.Current.Client.Role == LobbyRole.Traveler,
                    "Return to Lobby retains chosen roles");

                game = UnityEngine.Object.Instantiate(gamePrefab).GetComponent<CoopGameNetworkBridge>();
                game.GetComponent<NetworkObject>().Spawn();
                CoopGameNetworkBridge remoteGame = null;
                await Until(() =>
                {
                    foreach (var bridge in UnityEngine.Object.FindObjectsByType<CoopGameNetworkBridge>())
                        if (bridge.IsSpawned && bridge.NetworkManager == client) remoteGame = bridge;
                    return remoteGame != null && remoteGame.GetComponentInChildren<Canvas>() != null;
                }, "Role-based gameplay presentation initialized on late-spawned Client");
                Check(game.LocalRole == PlayerRole.Hacker && remoteGame.LocalRole == PlayerRole.Traveler, "Gameplay uses selected roles rather than Host/Client defaults");
                Check(Get<GameObject>(game.GetComponentInChildren<PreparationPreview>(true), "hacker").activeInHierarchy &&
                    !Get<GameObject>(game.GetComponentInChildren<PreparationPreview>(true), "traveler").activeInHierarchy,
                    "Host displays only selected Hacker panel");
                Check(Get<GameObject>(remoteGame.GetComponentInChildren<PreparationPreview>(true), "traveler").activeInHierarchy &&
                    !Get<GameObject>(remoteGame.GetComponentInChildren<PreparationPreview>(true), "hacker").activeInHierarchy,
                    "Client displays only selected Traveler panel");
                var prep = remoteGame.GetComponentInChildren<PreparationSession>(true);
                prep.RequestStart();
                prep.RequestIdentityEdit(IdentityField.Name, "must not apply");
                prep.AdvanceLocalTime(100);
                game.StartNewRound();
                await Task.Delay(150);
                Check(!prep.LocalSimulation && prep.Phase == EntryPhase.Waiting && string.IsNullOrEmpty(prep.Identity.Name) &&
                    game.CurrentState.Round == 0 && !game.GameplaySynchronizationEnabled,
                    "Online mode remains Waiting with local simulation and gameplay networking disabled");
                clientSession.UnregisterRoleSelection(clientRoles);
                Check(remoteGame.GetComponentInChildren<Canvas>() == null, "Missing role data keeps gameplay presentation inactive");
                clientSession.RegisterRoleSelection(clientRoles);
                Check(remoteGame.GetComponentInChildren<Canvas>() != null && remoteGame.GetComponentsInChildren<Canvas>(true).Length == 1,
                    "Late role data activates the existing Canvas without duplication");

                hostRoles.SetReady(true);
                await Confirm(hostRoles);
                client.Shutdown();
                await Until(() => !hostRoles.Current.Client.Connected, "Disconnect releases Client role");
                Check(!hostRoles.Current.Host.Ready && hostRoles.Current.Host.Role == LobbyRole.Hacker, "Disconnect resets survivor Ready and retains its role");
                await Task.Delay(250);
                Check(clientSession.RoleSelection == null, "Shutdown releases Client role-state reference");
                Check(client.StartClient(), "Client can reconnect");
                await Until(() => client.IsConnectedClient && clientSession.RoleSelection != null && hostRoles.Current.Client.Connected,
                    "Rejoined Client receives persistent role object");
                Check(hostRoles.Current.Client.Role == LobbyRole.Traveler && !hostRoles.Current.Client.Ready,
                    "Rejoin chooses free role without duplicating remaining Hacker");
                Check(UnityEngine.Object.FindObjectsByType<LobbyRoleSelection>().Length == 2,
                    "Exactly one role NetworkObject per loopback peer after reconnect");
                Debug.Log($"ROLE SELECTION CHECKS PASSED: {checks} checks over real NGO/Unity Transport loopback. Relay and NGO scene loading require the separate-player check.");
            }
            catch (Exception error) { Debug.LogError($"ROLE SELECTION CHECKS FAILED after {checks} checks: {error}"); }
            finally
            {
                if (client != null) client.Shutdown();
                hostSession.LeaveRoom();
                await Task.Delay(300);
                if (game != null) UnityEngine.Object.Destroy(game.gameObject);
                if (client != null) UnityEngine.Object.Destroy(client.gameObject);
                host.NetworkConfig.EnableSceneManagement = originalScenes;
                host.NetworkConfig.ForceSamePrefabs = originalSame;
                protocolField.SetValue(transport, originalProtocol);
                running = false;
            }
        }

        private static async Task Confirm(LobbyRoleSelection roles)
        {
            await Until(() => !roles.HasPendingRequest, "Request confirmed by Host");
            await Task.Delay(40);
        }

        private static async Task Until(Func<bool> condition, string label)
        {
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (!condition())
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(label);
                await Task.Delay(20);
            }
            Check(true, label);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks++;
        }
        private static void Call(object target, string method, params object[] arguments) => target.GetType().GetMethod(method, Private).Invoke(target, arguments);
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
    }
}
#endif
