#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CarryOn.Taylor;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.Multiplayer.Editor
{
    public static class CoopGameRelayChecks
    {
        private const string OutputKey = "CoopGameRelayCheckOutput";

        [MenuItem("Tools/Cooperative Game/Build Relay Check Player (macOS)")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building the check player.");
            string folder = Path.Combine(Path.GetTempPath(), "coop-relay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                target = BuildTarget.StandaloneOSX,
                locationPathName = Path.Combine(folder, "CoopCheck.app"),
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Relay check player build failed: {report.summary.result}");
            SessionState.SetString(OutputKey, folder);
            Debug.Log("COOP RELAY CHECK PLAYER BUILT: " + folder);
        }

        [MenuItem("Tools/Cooperative Game/Run Relay Scene Checks (Play Mode)")]
        public static async void Run()
        {
            string folder = SessionState.GetString(OutputKey, "");
            if (!EditorApplication.isPlaying || string.IsNullOrEmpty(folder))
            {
                Debug.LogWarning("Build Relay Check Player in Edit Mode, then enter Play Mode in MultiplayerTest.");
                return;
            }
            var session = UnityEngine.Object.FindAnyObjectByType<NetworkSessionManager>();
            if (session == null || NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("Relay scene checks require an idle lobby.");
                return;
            }
            Process clientProcess = null;
            string marker = Path.Combine(folder, "client-result.txt");
            try
            {
                session.CreateRoom();
                await Until(() => session.State == ConnectionState.Connected, "Host Relay allocation/authentication", 45);
                string executable = Directory.GetFiles(Path.Combine(folder, "CoopCheck.app/Contents/MacOS")).Single();
                clientProcess = Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    Arguments = $"-batchmode -nographics -logFile \"{folder}/client.log\" --coop-check-code={session.JoinCode} --coop-check-report=\"{marker}\""
                });
                await Until(() => { Marker(marker); return session.CanStartGame; }, "Second Relay peer connected", 60);
                session.StartGame();
                CoopGameNetworkBridge bridge = null;
                await Until(() =>
                {
                    bridge = UnityEngine.Object.FindAnyObjectByType<CoopGameNetworkBridge>();
                    return bridge != null && bridge.IsSpawned && bridge.CurrentState.Round == 1;
                }, "Both peers loaded GameScene", 45);
                if (bridge.LocalRole != PlayerRole.Traveler) throw new InvalidOperationException("Host role is not Traveler");
                await Until(() => Marker(marker) == "printed", "Hacker identity editing and print", 30);
                var preparation = bridge.GetComponentInChildren<PreparationSession>(true);
                var interview = bridge.GetComponentInChildren<InterviewController>(true);
                await Until(() => preparation.Phase == EntryPhase.Interview, "Real authoritative preparation deadline", 90);
                if (preparation.PrintedIdentity.Name != "Relay Alice") throw new InvalidOperationException("Final identity mismatch");
                interview.Shake();
                await Task.Delay(250);
                interview.SubmitAnswer(" relay alice ");
                await Task.Delay(100);
                interview.SubmitAnswer("CANADA");
                await Task.Delay(100);
                interview.SubmitAnswer("believer");
                await Until(() => Marker(marker) == "victory", "Remote help/reputation/victory", 30);
                session.ReturnToLobby();
                await Until(() => Marker(marker) == "lobby", "Both peers returned to Lobby", 30);
                session.LeaveRoom();
                await Until(() => Marker(marker) == "passed", "Client notified of Host shutdown", 30);
                Debug.Log("COOP RELAY SCENE CHECKS PASSED: separate Editor/standalone peers, Relay authentication/allocation/join, " +
                    "NGO additive GameScene synchronization, role UIs, identity editing/print, real countdown, " +
                    "Shake/help/reputation, victory, return to Lobby, Host disconnect. Client log: " + folder + "/client.log");
            }
            catch (Exception error) { Debug.LogError("COOP RELAY SCENE CHECKS FAILED: " + error + "; report=" + Marker(marker)); }
            finally
            {
                if (session != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) session.LeaveRoom();
                clientProcess?.Dispose();
            }
        }

        private static string Marker(string path)
        {
            if (!File.Exists(path)) return string.Empty;
            string result = File.ReadAllText(path);
            if (result.StartsWith("failed:", StringComparison.Ordinal)) throw new InvalidOperationException(result);
            return result;
        }

        private static async Task Until(Func<bool> condition, string label, int seconds)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (!condition())
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(label);
                await Task.Delay(30);
            }
        }
    }
}
#endif
