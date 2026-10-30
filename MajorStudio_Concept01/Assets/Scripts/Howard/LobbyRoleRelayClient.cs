#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CarryOn.Taylor;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Multiplayer
{
    // Explicit development-player fixture; no component is created for normal launches.
    public sealed class LobbyRoleRelayClient : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if (!string.IsNullOrEmpty(Argument("--role-check-code=")))
                new GameObject("RoleRelayCheckClient").AddComponent<LobbyRoleRelayClient>();
        }

        private async void Start()
        {
            DontDestroyOnLoad(gameObject);
            string report = Argument("--role-check-report=");
            string command = Argument("--role-check-command=");
            string code = Argument("--role-check-code=");
            try
            {
                await UnityServices.InitializeAsync(new InitializationOptions().SetProfile("rolecheck" + Guid.NewGuid().ToString("N").Substring(0, 16)));
                var session = FindAnyObjectByType<NetworkSessionManager>();
                session.JoinRoom(code);
                await Until(() => session.State == ConnectionState.Connected && session.RoleSelection != null &&
                    session.RoleSelection.Current.Client.Connected, "Client joined with role data");
                var roles = session.RoleSelection;
                Require(roles.Current.Host.Role == LobbyRole.Traveler && roles.Current.Client.Role == LobbyRole.Hacker, "Initial Host/Client roles");
                File.WriteAllText(report, "default");
                await Until(() => Read(command) == "release-client", "Release command");
                roles.ReleaseRole();
                await Until(() => !roles.HasPendingRequest && roles.Current.Client.Role == LobbyRole.Unassigned, "Client released role");
                File.WriteAllText(report, "released");
                await Until(() => Read(command) == "select-traveler", "Swap command");
                roles.SelectRole(LobbyRole.Traveler);
                await Until(() => !roles.HasPendingRequest && roles.Current.Client.Role == LobbyRole.Traveler, "Client selected Traveler");
                Require(roles.Current.Host.Role == LobbyRole.Hacker, "Host now owns Hacker");
                File.WriteAllText(report, "swapped");
                await Until(() => Read(command) == "ready", "Ready command");
                roles.SetReady(true);
                await Until(() => !roles.HasPendingRequest && roles.Current.BothReady, "Both Ready");
                Require(!session.CanStartGame, "Client cannot Start");
                session.StartGame();
                Require(!session.IsInGame, "Client Start request rejected");
                File.WriteAllText(report, "ready");
                CoopGameNetworkBridge bridge = null;
                await Until(() =>
                {
                    bridge = FindAnyObjectByType<CoopGameNetworkBridge>();
                    return bridge != null && bridge.IsSpawned && bridge.GetComponentInChildren<Canvas>() != null;
                }, "Client loaded synchronized GameScene");
                Require(bridge.LocalRole == PlayerRole.Traveler && !bridge.GameplaySynchronizationEnabled, "Selected Traveler applied in role-only stage");
                var preview = bridge.GetComponentInChildren<PreparationPreview>(true);
                Require(Panel(preview, "traveler").activeInHierarchy && !Panel(preview, "hacker").activeInHierarchy, "Only Traveler panel visible");
                var prep = bridge.GetComponentInChildren<PreparationSession>(true);
                prep.RequestStart();
                prep.RequestIdentityEdit(IdentityField.Name, "ignored");
                prep.AdvanceLocalTime(100);
                bridge.StartNewRound();
                await Task.Delay(200);
                Require(!prep.LocalSimulation && prep.Phase == EntryPhase.Waiting && string.IsNullOrEmpty(prep.Identity.Name) &&
                    bridge.CurrentState.Round == 0, "Gameplay remains non-interactive Waiting");
                Require(bridge.GetComponentsInChildren<Canvas>(true).Length == 1 && FindObjectsByType<NetworkManager>().Length == 1 &&
                    FindObjectsByType<EventSystem>().Length == 1 && FindObjectsByType<LobbyRoleSelection>().Length == 1,
                    "No duplicate Canvas, NetworkManager, EventSystem or role NetworkObject");
                roles.SelectRole(LobbyRole.Hacker);
                await Until(() => !roles.HasPendingRequest, "Locked role request confirmed");
                Require(roles.Current.Client.Role == LobbyRole.Traveler, "Role locked during game");
                File.WriteAllText(report, "game");
                await Until(() => bridge == null && !session.IsInGame && !roles.Current.GameStarted, "Returned to Lobby");
                Require(roles.Current.Client.Role == LobbyRole.Traveler && roles.Current.Host.Role == LobbyRole.Hacker &&
                    !roles.Current.Client.Ready && !roles.Current.Host.Ready, "Roles retained and Ready reset in Lobby");
                File.WriteAllText(report, "lobby");
                await Until(() => Read(command) == "disconnect", "Disconnect command");
                session.LeaveRoom();
                await Until(() => !NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.ShutdownInProgress &&
                    session.RoleSelection == null, "Client shutdown cleared role object");
                File.WriteAllText(report, "disconnected");
                await Until(() => Read(command) == "rejoin", "Rejoin command");
                session.JoinRoom(code);
                await Until(() => session.State == ConnectionState.Connected && session.RoleSelection != null &&
                    session.RoleSelection.Current.Client.Connected, "Client rejoined Relay");
                roles = session.RoleSelection;
                Require(roles.Current.Host.Role == LobbyRole.Hacker && roles.Current.Client.Role == LobbyRole.Traveler &&
                    !roles.Current.Host.Ready && !roles.Current.Client.Ready, "Rejoin preserves unique roles and clears Ready");
                File.WriteAllText(report, "rejoined");
                await Until(() => session.State != ConnectionState.Connected && session.RoleSelection == null, "Host disconnect notification and cleanup");
                File.WriteAllText(report, "passed");
                Debug.Log("ROLE RELAY CLIENT CHECK PASSED");
            }
            catch (Exception error)
            {
                File.WriteAllText(report, "failed: " + error);
                Debug.LogError(error);
            }
            finally { Application.Quit(); }
        }

        private static GameObject Panel(PreparationPreview preview, string field) =>
            (GameObject)typeof(PreparationPreview).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
        private static string Argument(string prefix)
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith(prefix, StringComparison.Ordinal)) return arg.Substring(prefix.Length);
            return string.Empty;
        }
        private static string Read(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        private static async Task Until(Func<bool> condition, string message)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException(message);
                await Task.Delay(30);
            }
        }
    }
}
#endif
