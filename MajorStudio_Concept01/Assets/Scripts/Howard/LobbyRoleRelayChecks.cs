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
        [MenuItem("Tools/Multiplayer Cutscene/Build Check Player (macOS)")]
        [MenuItem("Tools/Role Selection/Build Check Player (macOS)")]
        public static void Build() => CoopGameRelayChecks.Build();

        [MenuItem("Tools/Multiplayer Cutscene/Run Relay Scene Checks (Play Mode)")]
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
                await Until(() => ReadReport(report) == "joined", "Client joined Relay with unassigned slots");
                var roles = session.RoleSelection;
                var button = GameObject.Find("StartGameButton").GetComponent<Button>();
                if (!button.interactable || !session.CanStartGame) throw new InvalidOperationException("Two connections did not enable Start");
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "lobby.png"));
                await Task.Delay(250);
                button.onClick.Invoke();
                button.onClick.Invoke();
                await Until(() => ReadReport(report) == "selection" && roles.Current.Stage == SessionStage.CharacterSelection,
                    "Both peers completed the three default-duration comics");
                var view = roles.GetComponentInChildren<CutsceneCarousel>();
                view.transform.Find("Content/CharacterSelection/BriefcaseButton").GetComponent<Button>().onClick.Invoke();
                await Until(() => roles.Current.Host.Role == LobbyRole.Hacker, "Host clicked Briefcase");
                File.WriteAllText(command, "select-traveler");
                await Until(() => ReadReport(report) == "countdown" && roles.Current.CountdownActive, "Automatic countdown active on both peers");
                uint canceledGeneration = roles.Current.CountdownGeneration;
                double canceledDeadline = roles.Current.CountdownEndsAt;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "character-selection.png"));
                await Task.Delay(300);
                File.WriteAllText(command, "deselect");
                await Until(() => ReadReport(report) == "canceled" && !roles.Current.CountdownActive, "Client deselection canceled both stopwatches");
                await Until(() => roles.ServerNow > canceledDeadline + 0.2, "Old countdown deadline passed");
                if (session.IsInGame || roles.Current.GameStarted || roles.TryLockForStart(canceledGeneration))
                    throw new InvalidOperationException("Canceled timer started gameplay");
                File.WriteAllText(command, "reselect");
                await Until(() => ReadReport(report) == "game", "Client loaded GameScene and selected Traveler UI");
                var bridge = UnityEngine.Object.FindAnyObjectByType<CoopGameNetworkBridge>();
                if (bridge == null || bridge.LocalRole != PlayerRole.Hacker)
                    throw new InvalidOperationException("Host did not enter selected Hacker presentation");
                var preview = bridge.GetComponentInChildren<PreparationPreview>(true);
                var hacker = (GameObject)typeof(PreparationPreview).GetField("hacker", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
                if (!hacker.activeInHierarchy || bridge.GetComponentsInChildren<Canvas>(true).Length != 1)
                    throw new InvalidOperationException("Host Hacker panel or Canvas count incorrect");
                session.ReturnToLobby();
                await Until(() => ReadReport(report) == "lobby", "Both returned to Lobby with unassigned roles");
                if (!session.CanStartGame) throw new InvalidOperationException("Two connected players cannot restart introduction");
                File.WriteAllText(command, "disconnect");
                await Until(() => ReadReport(report) == "disconnected" && !roles.Current.Client.Connected,
                    "Client disconnect released role");
                File.WriteAllText(command, "rejoin");
                await Until(() => ReadReport(report) == "rejoined", "Client rejoined with unassigned slot");
                session.LeaveRoom();
                await Until(() => ReadReport(report) == "passed", "Client received Host disconnect");
                Debug.Log("CUTSCENE RELAY SCENE CHECKS PASSED: separate Editor/standalone peers; Relay create/join, two-player Start, " +
                    "three comics with default holds, button-based selection/deselection, countdown cancellation past its original deadline, " +
                    "fresh automatic countdown, synchronized GameScene with Host Hacker/Client Traveler, return to Lobby, disconnect/rejoin, " +
                    "Host shutdown and no duplicate gameplay objects. Evidence folder: " + folder);
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
            double deadline = EditorApplication.timeSinceStartup + 120;
            while (!condition())
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(label);
                await Task.Delay(30);
            }
        }
    }
}
#endif
