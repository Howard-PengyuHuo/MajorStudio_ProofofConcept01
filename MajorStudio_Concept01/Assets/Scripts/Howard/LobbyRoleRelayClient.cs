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
using UnityEngine.UI;

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
                CheckLobbyBackground(session);
                Require(roles.Current.Host.Role == LobbyRole.Unassigned && roles.Current.Client.Role == LobbyRole.Unassigned, "Lobby has network slots only");
                File.WriteAllText(report, "joined");
                await Until(() => roles.Current.Stage == SessionStage.CharacterSelection, "Three comics completed");
                var view = roles.GetComponentInChildren<CutsceneCarousel>();
                Require(!session.IsInGame && !roles.Current.CountdownActive, "No premature gameplay or countdown");
                File.WriteAllText(report, "selection");
                await Until(() => Read(command) == "select-traveler", "Selection command");
                var personButton = view.transform.Find("Content/CharacterSelection/PersonButton").GetComponent<Button>();
                personButton.onClick.Invoke();
                await Until(() => !roles.HasPendingRequest && roles.Current.Client.Role == LobbyRole.Traveler, "Client selected Traveler");
                Require(roles.Current.Host.Role == LobbyRole.Hacker, "Host now owns Hacker");
                await Until(() => roles.Current.CountdownActive, "Automatic countdown started");
                var stopwatch = view.transform.Find("Content/CharacterSelection/StopwatchImage").gameObject;
                Require(stopwatch.activeInHierarchy, "Client stopwatch visible");
                var marker = personButton.transform.Find("PersonMarker").GetComponent<Image>();
                Require(marker.gameObject.activeInHierarchy && marker.sprite != null && marker.sprite.name == "P2_0",
                    "Client displays the P2 marker image on Person");
                Require(!session.CanStartGame, "Client cannot Start");
                session.StartGame();
                Require(!session.IsInGame, "Client Start request rejected");
                File.WriteAllText(report, "countdown");
                uint generation = roles.Current.CountdownGeneration;
                await Until(() => Read(command) == "deselect", "Deselect command");
                personButton.onClick.Invoke();
                await Until(() => !roles.HasPendingRequest && !roles.Current.CountdownActive &&
                    roles.Current.Client.Role == LobbyRole.Unassigned, "Deselect accepted");
                Require(!stopwatch.activeInHierarchy, "Canceled stopwatch hidden");
                File.WriteAllText(report, "canceled");
                await Until(() => Read(command) == "reselect", "Fresh selection command");
                personButton.onClick.Invoke();
                await Until(() => roles.Current.CountdownActive, "Fresh countdown active");
                Require(roles.Current.CountdownGeneration > generation &&
                    roles.Current.CountdownEndsAt - roles.ServerNow > 8, "Fresh full-duration countdown received");
                CoopGameNetworkBridge bridge = null;
                await Until(() =>
                {
                    bridge = FindAnyObjectByType<CoopGameNetworkBridge>();
                    return bridge != null && bridge.IsSpawned && bridge.GetComponentInChildren<Canvas>() != null;
                }, "Client loaded synchronized GameScene");
                Require(bridge.LocalRole == PlayerRole.Traveler, "Selected Person mapped to Traveler");
                var preview = bridge.GetComponentInChildren<PreparationPreview>(true);
                Require(Panel(preview, "traveler").activeInHierarchy && !Panel(preview, "hacker").activeInHierarchy, "Only Traveler panel visible");
                Require(!bridge.GetComponentInChildren<PreparationSession>(true).LocalSimulation,
                    "Existing online gameplay retains network authority");
                Require(bridge.GetComponentsInChildren<Canvas>(true).Length == 1 && FindObjectsByType<NetworkManager>().Length == 1 &&
                    FindObjectsByType<EventSystem>().Length == 1 && FindObjectsByType<LobbyRoleSelection>().Length == 1,
                    "No duplicate Canvas, NetworkManager, EventSystem or role NetworkObject");
                roles.SelectRole(LobbyRole.Hacker);
                await Until(() => !roles.HasPendingRequest, "Locked role request confirmed");
                Require(roles.Current.Client.Role == LobbyRole.Traveler, "Role locked during game");
                File.WriteAllText(report, "game");
                await Until(() => bridge == null && !session.IsInGame && !roles.Current.GameStarted, "Returned to Lobby");
                Require(roles.Current.Client.Role == LobbyRole.Unassigned && roles.Current.Host.Role == LobbyRole.Unassigned &&
                    roles.Current.Stage == SessionStage.Lobby, "Roles reset for replay in Lobby");
                CheckLobbyBackground(session);
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
                CheckLobbyBackground(session);
                Require(roles.Current.Host.Role == LobbyRole.Unassigned && roles.Current.Client.Role == LobbyRole.Unassigned,
                    "Rejoin supplies unassigned network slot");
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

        private static void CheckLobbyBackground(NetworkSessionManager session)
        {
            var ui = session.GetComponent<LobbyUI>();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var image = (Image)typeof(LobbyUI).GetField("lobbyPanelImage", flags).GetValue(ui);
            var sprite = (Sprite)typeof(LobbyUI).GetField("playerTwoLobbyBackground", flags).GetValue(ui);
            Require(!session.IsHost && session.State == ConnectionState.Connected && session.Players.Count == 2,
                "Relay Client connected with two players");
            Require(sprite != null && sprite.name == "Lobby Main Interface_Player2_0" && image.sprite == sprite &&
                image.overrideSprite == sprite && image.gameObject.activeInHierarchy && image.gameObject.scene.IsValid(),
                $"Client passport mismatch: sprite={image.sprite?.name}, visible={image.overrideSprite?.name}, expected={sprite?.name}");
            Debug.Log("CLIENT LOBBY BACKGROUND VERIFIED: " + image.overrideSprite.name);
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
            DateTime deadline = DateTime.UtcNow.AddSeconds(120);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException(message);
                await Task.Delay(30);
            }
        }
    }
}
#endif
