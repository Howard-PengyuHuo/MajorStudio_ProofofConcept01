#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CarryOn.Taylor;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace Game.Multiplayer.Editor
{
    public static class LobbyRoleRelayChecks
    {
        private static bool running;
        [MenuItem("Tools/Role Selection/Build Check Player (macOS)")]
        public static void Build() => CoopGameRelayChecks.Build();

        [MenuItem("Tools/Role Selection/Run Relay Scene Checks (Play Mode)")]
        public static async void Run()
        {
            string folder = SessionState.GetString("CoopGameRelayCheckOutput", "");
            var session = UnityEngine.Object.FindAnyObjectByType<NetworkSessionManager>();
            if (!EditorApplication.isPlaying || running || session == null || NetworkManager.Singleton.IsListening ||
                string.IsNullOrEmpty(folder))
            {
                Debug.LogWarning("Build the role check player in Edit Mode, then enter Play Mode in an idle MultiplayerTest Lobby.");
                return;
            }
            running = true;
            Process client = null;
            string report = Path.Combine(folder, "role-client-result.txt");
            string command = Path.Combine(folder, "role-client-command.txt");
            try
            {
                File.WriteAllText(report, string.Empty);
                File.WriteAllText(command, string.Empty);
                session.CreateRoom();
                await Until(() => session.State == ConnectionState.Connected && session.RoleSelection != null, "Host Relay room created");
                string executable = Directory.GetFiles(Path.Combine(folder, "CoopCheck.app/Contents/MacOS")).Single();
                client = Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    Arguments = $"-batchmode -nographics -logFile \"{folder}/role-client.log\" --role-check-code={session.JoinCode} " +
                        $"--role-check-report=\"{report}\" --role-check-command=\"{command}\""
                });
                await Until(() => ReadReport(report) == "default", "Client joined Relay with default roles");
                var roles = session.RoleSelection;
                roles.SetReady(true);
                await Until(() => !roles.HasPendingRequest, "Host Ready confirmed");
                if (session.CanStartGame) throw new InvalidOperationException("One Ready player enabled Start");
                session.StartGame();
                if (session.IsInGame) throw new InvalidOperationException("Started with one Ready player");
                roles.SetReady(false);
                await Until(() => !roles.HasPendingRequest, "Cancel Ready confirmed");
                roles.ReleaseRole();
                await Until(() => !roles.HasPendingRequest, "Host released Traveler");
                File.WriteAllText(command, "release-client");
                await Until(() => ReadReport(report) == "released", "Client released Hacker");
                roles.SelectRole(LobbyRole.Hacker);
                await Until(() => !roles.HasPendingRequest, "Host selected Hacker");
                File.WriteAllText(command, "select-traveler");
                await Until(() => ReadReport(report) == "swapped", "Client selected Traveler");
                roles.SetReady(true);
                await Until(() => !roles.HasPendingRequest, "Host readied chosen role");
                File.WriteAllText(command, "ready");
                await Until(() => ReadReport(report) == "ready" && session.CanStartGame, "Both Ready enables Start");
                var button = GameObject.Find("StartGameButton").GetComponent<Button>();
                if (!button.interactable) throw new InvalidOperationException("Start button did not update after Ready confirmation");
                button.onClick.Invoke();
                button.onClick.Invoke();
                await Until(() => ReadReport(report) == "game", "Client loaded GameScene and selected Traveler UI");
                var bridge = UnityEngine.Object.FindAnyObjectByType<CoopGameNetworkBridge>();
                if (bridge == null || bridge.LocalRole != PlayerRole.Hacker || bridge.GameplaySynchronizationEnabled)
                    throw new InvalidOperationException("Host did not enter role-only Hacker presentation");
                var preview = bridge.GetComponentInChildren<PreparationPreview>(true);
                var hacker = (GameObject)typeof(PreparationPreview).GetField("hacker", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
                if (!hacker.activeInHierarchy || bridge.GetComponentsInChildren<Canvas>(true).Length != 1)
                    throw new InvalidOperationException("Host Hacker panel or Canvas count incorrect");
                session.ReturnToLobby();
                await Until(() => ReadReport(report) == "lobby", "Both returned to Lobby with preserved roles");
                if (session.CanStartGame) throw new InvalidOperationException("Return to Lobby did not reset Ready");
                File.WriteAllText(command, "disconnect");
                await Until(() => ReadReport(report) == "disconnected" && !roles.Current.Client.Connected,
                    "Client disconnect released role");
                if (roles.Current.Host.Ready) throw new InvalidOperationException("Remaining player stayed Ready");
                roles.SetReady(true);
                await Until(() => !roles.HasPendingRequest, "Remaining Host Ready confirmed");
                File.WriteAllText(command, "rejoin");
                await Until(() => ReadReport(report) == "rejoined", "Client rejoined with free role and Ready reset");
                session.LeaveRoom();
                await Until(() => ReadReport(report) == "passed", "Client received Host disconnect");
                Debug.Log("ROLE RELAY SCENE CHECKS PASSED: separate Editor/standalone peers; Relay create/join, initial and swapped roles, " +
                    "Ready/Cancel Ready and Start gating, duplicate Start protection, synchronized GameScene with Host Hacker/Client Traveler, " +
                    "Waiting-only gameplay, return to Lobby, disconnect/rejoin role cleanup, and no duplicate runtime objects. Client log: " + folder + "/role-client.log");
            }
            catch (Exception error) { Debug.LogError("ROLE RELAY SCENE CHECKS FAILED: " + error); }
            finally
            {
                if (session != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) session.LeaveRoom();
                if (client != null)
                {
                    await Task.Delay(500);
                    if (!client.HasExited) client.Kill();
                    client.Dispose();
                }
                running = false;
            }
        }

        private static string ReadReport(string path)
        {
            string report = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            if (report.StartsWith("failed:", StringComparison.Ordinal)) throw new InvalidOperationException(report);
            return report;
        }
        private static async Task Until(Func<bool> condition, string label)
        {
            double deadline = EditorApplication.timeSinceStartup + 60;
            while (!condition())
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(label);
                await Task.Delay(30);
            }
        }
    }
}
#endif
