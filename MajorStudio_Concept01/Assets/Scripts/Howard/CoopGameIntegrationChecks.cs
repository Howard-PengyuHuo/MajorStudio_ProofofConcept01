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
    // Explicit Play Mode smoke test using two real NGO peers over Unity Transport loopback.
    // Scene readiness is supplied by the fixture; NGO scene synchronization is tested separately.
    public static class CoopGameIntegrationChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static bool running;
        private static int checks;

        [MenuItem("Tools/Cooperative Game/Run Loopback Gameplay Checks (Play Mode)")]
        public static async void Run()
        {
            if (!EditorApplication.isPlaying || running)
            {
                Debug.LogWarning("Enter Play Mode in MultiplayerTest before running gameplay checks.");
                return;
            }
            NetworkManager host = NetworkManager.Singleton;
            if (host == null || host.IsListening)
            {
                Debug.LogWarning("Gameplay checks require an idle NetworkManager (leave any existing room first).");
                return;
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CoopGameSceneSetup.BridgePrefabPath);
            if (prefab == null) throw new InvalidOperationException("Run Configure Integration Assets first.");
            running = true;
            checks = 0;
            NetworkManager client = null;
            CoopGameNetworkBridge authority = null;
            GameObject offline = null;
            bool sceneManagement = host.NetworkConfig.EnableSceneManagement;
            bool samePrefabs = host.NetworkConfig.ForceSamePrefabs;
            var hostTransport = host.GetComponent<UnityTransport>();
            var protocolField = typeof(UnityTransport).GetField("m_ProtocolType", Private);
            object protocol = protocolField.GetValue(hostTransport);
            GameObject lobby = GameObject.Find("MultiplayerCanvas");
            try
            {
                protocolField.SetValue(hostTransport, UnityTransport.ProtocolType.UnityTransport);
                hostTransport.SetConnectionData("127.0.0.1", 17983);
                host.NetworkConfig.EnableSceneManagement = false;
                host.NetworkConfig.ForceSamePrefabs = false;
                if (!host.NetworkConfig.Prefabs.Contains(prefab)) host.AddNetworkPrefab(prefab);
                Check(host.StartHost(), "NGO Host started on loopback");
                var clientObject = new GameObject("LoopbackCheckClient");
                clientObject.SetActive(false);
                var clientTransport = clientObject.AddComponent<UnityTransport>();
                client = clientObject.AddComponent<NetworkManager>();
                client.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = clientTransport, EnableSceneManagement = false,
                    ConnectionApproval = true, ForceSamePrefabs = false
                };
                clientObject.SetActive(true);
                clientTransport.SetConnectionData("127.0.0.1", 17983);
                client.AddNetworkPrefab(prefab);
                Check(client.StartClient(), "NGO Client started");
                await Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "Host/Client connected");
                if (lobby != null) lobby.SetActive(false);
                authority = UnityEngine.Object.Instantiate(prefab).GetComponent<CoopGameNetworkBridge>();
                authority.GetComponent<NetworkObject>().Spawn();
                CoopGameNetworkBridge remote = null;
                await Until(() =>
                {
                    foreach (var bridge in UnityEngine.Object.FindObjectsByType<CoopGameNetworkBridge>())
                        if (bridge.IsSpawned && bridge.NetworkManager == client) remote = bridge;
                    return remote != null && remote.CurrentState.Revision != 0;
                }, "Bridge spawned and initial snapshot replicated");
                Set(authority, "ready", true);
                authority.StartNewRound();
                await Same(authority, remote);
                var hostPreparation = authority.GetComponentInChildren<PreparationSession>(true);
                var clientPreparation = remote.GetComponentInChildren<PreparationSession>(true);
                var hostInterview = authority.GetComponentInChildren<InterviewController>(true);
                var clientInterview = remote.GetComponentInChildren<InterviewController>(true);
                int hostFinished = 0, clientFinished = 0, helps = 0;
                hostInterview.Finished += () => hostFinished++;
                clientInterview.Finished += () => clientFinished++;
                remote.HelpRequested += () => helps++;
                Check(authority.LocalRole == PlayerRole.Traveler && remote.LocalRole == PlayerRole.Hacker, "Host=Traveler, Client=Hacker");
                Check(!hostPreparation.LocalSimulation && !clientPreparation.LocalSimulation, "Online local simulation disabled");
                Check(Get<GameObject>(authority.GetComponentInChildren<PreparationPreview>(true), "traveler").activeSelf &&
                    Get<GameObject>(remote.GetComponentInChildren<PreparationPreview>(true), "hacker").activeSelf,
                    "Each peer shows its assigned preparation UI");
                Check(authority.GetComponentsInChildren<Canvas>(true).Length == 1 &&
                    remote.GetComponentsInChildren<Canvas>(true).Length == 1, "One gameplay Canvas per peer");
                Check(Mathf.Abs(hostPreparation.RemainingSeconds - clientPreparation.RemainingSeconds) < 0.3f,
                    "Countdown derives from a shared server deadline");

                hostPreparation.RequestIdentityEdit(IdentityField.Name, "Unauthorized");
                await Task.Delay(150);
                Check(string.IsNullOrEmpty(hostPreparation.Identity.Name), "Traveler identity edits rejected");
                clientPreparation.RequestIdentityEdit(IdentityField.Name, "Alice");
                clientPreparation.RequestIdentityEdit(IdentityField.Nationality, "Canada");
                clientPreparation.RequestIdentityEdit(IdentityField.Age, "28");
                clientPreparation.RequestIdentityEdit(IdentityField.LicenseNumber, "ID-42");
                await Until(() => hostPreparation.Identity.IsComplete && clientPreparation.CanPrint, "All four identity fields replicated");
                clientPreparation.RequestPrint();
                await Until(() => hostPreparation.HasPrintedIdentity && clientPreparation.HasPrintedIdentity, "Print replicated");
                Check(hostPreparation.PrintedIdentity.Name == "Alice", "Printed identity matches current identity");
                clientPreparation.RequestIdentityEdit(IdentityField.Name, "Changed after print");
                clientPreparation.RequestPrint();
                await Task.Delay(150);
                Check(hostPreparation.Identity.Name == "Alice", "Printed identity locked and repeat Print ignored");
                Expire(authority);
                await Until(() => clientPreparation.Phase == EntryPhase.Interview, "Interview phase replicated atomically with printed identity");
                Check(clientPreparation.PrintedIdentity.Name == "Alice" && clientInterview.Reputation == 50, "Interview starts with correct identity and reputation");
                Check(!clientInterview.EvaluateAnswer("Alice") && !clientInterview.ApplyShake(),
                    "Remote instance cannot run authority-only evaluation or reputation changes");
                Check(Get<GameObject>(remote.GetComponentInChildren<PreparationPreview>(true), "hackerInterview").activeSelf,
                    "Hacker interview desktop visible");
                hostInterview.SubmitAnswer("   ALICE  ");
                await Until(() => clientInterview.QuestionIndex == 1, "Case/whitespace-insensitive answer advances both peers");
                Replay(authority, "Answer", 0, "Alice", false);
                await Task.Delay(150);
                Check(hostInterview.QuestionIndex == 1, "Duplicate answer request ignored");
                Replay(authority, "Answer", 0, "Alice", true);
                await Task.Delay(150);
                Check(hostInterview.QuestionIndex == 1 && !hostInterview.IsFinished, "Stale question index rejected");
                Replay(remote, "Answer", 1, "Canada", true);
                await Task.Delay(150);
                Check(hostInterview.QuestionIndex == 1, "Hacker cannot submit Traveler answers");
                hostInterview.SubmitAnswer("Canada");
                await Until(() => clientInterview.QuestionIndex == 2, "Second answer replicated");
                hostInterview.SubmitAnswer("believer");
                await Until(() => clientInterview.IsFinished, "Victory replicated");
                Check(hostInterview.EntryApproved && clientInterview.EntryApproved, "Both peers approve entry");
                Call(remote, "ApplySnapshot", remote.CurrentState);
                Call(remote, "ApplySnapshot", remote.CurrentState);
                Check(hostFinished == 1 && clientFinished == 1, "Duplicate final snapshots do not repeat Finished callbacks");
                Check(Get<GameObject>(clientInterview, "gameOverPanel").activeSelf, "Remote GameOver UI displayed");

                authority.StartNewRound();
                await Until(() => remote.CurrentState.Round == 2, "Second round replicated");
                Check(!clientInterview.IsFinished && clientInterview.QuestionIndex == 0 && clientInterview.Reputation == 50 &&
                    !clientPreparation.HasPrintedIdentity && !clientPreparation.Identity.IsComplete, "New round resets identity, print, progress, result and reputation");
                uint round = authority.CurrentState.Round;
                authority.StartNewRound();
                Check(authority.CurrentState.Round == round, "Active round cannot start twice");
                Expire(authority);
                await Until(() => clientPreparation.Phase == EntryPhase.Interview, "Second Interview started without inventing a missing-print penalty");
                authority.GetComponentInChildren<PreparationPreview>(true).SetLocalRole(PlayerRole.Hacker);
                Set(hostInterview, "wrongAnswerReason", new string('R', 240));
                Replay(authority, "Answer", 0, "wrong answer", true);
                await Until(() => clientInterview.IsFinished, "Wrong answer failure replicated");
                Check(hostInterview.IsFinished, "Authority evaluates answers while Traveler panel is hidden");
                Check(!hostInterview.EntryApproved && !clientInterview.EntryApproved &&
                    hostInterview.ResultReason == clientInterview.ResultReason, "Both peers receive same failure reason");

                authority.StartNewRound();
                await Until(() => remote.CurrentState.Round == 3, "Third round replicated");
                ReplayRound(remote, 1, "Print");
                await Task.Delay(100);
                Check(!hostPreparation.HasPrintedIdentity, "Stale previous-round request ignored");
                Expire(authority);
                await Until(() => clientPreparation.Phase == EntryPhase.Interview, "Third Interview started");
                Replay(remote, "Shake", 0, "", true);
                await Task.Delay(150);
                Check(hostInterview.Reputation == 50, "Hacker cannot spend Traveler reputation");
                hostInterview.Shake();
                await Until(() => clientInterview.Reputation == 30, "Shake 50 to 30 replicated");
                Replay(authority, "Shake", 0, "", false);
                await Task.Delay(150);
                Check(hostInterview.Reputation == 30 && helps == 1, "Duplicate Shake ignored; Hacker notified exactly once");
                hostInterview.Shake();
                await Until(() => clientInterview.Reputation == 10, "Shake 30 to 10 replicated");
                hostInterview.Shake();
                await Until(() => clientInterview.IsFinished && clientInterview.Reputation == 0, "Shake 10 to 0 ends both games");
                Check(!clientInterview.EntryApproved && helps == 3, "Reputation failure and all help events synchronized");
                hostInterview.Shake();
                hostInterview.SubmitAnswer("believer");
                await Task.Delay(100);
                Check(hostInterview.Reputation == 0 && hostFinished == 3 && clientFinished == 3, "Post-result actions blocked without repeated results");

                authority.StartNewRound();
                await Until(() => remote.CurrentState.Round == 4, "Fourth round replicated for boundary-input check");
                string fullLengthName = new string('\u00e9', 64);
                clientPreparation.RequestIdentityEdit(IdentityField.Name, fullLengthName);
                clientPreparation.RequestIdentityEdit(IdentityField.Nationality, "Canada");
                clientPreparation.RequestIdentityEdit(IdentityField.Age, "28");
                clientPreparation.RequestIdentityEdit(IdentityField.LicenseNumber, "ID-42");
                await Until(() => clientPreparation.CanPrint, "64-character Unicode identity transported intact");
                clientPreparation.RequestPrint();
                await Until(() => hostPreparation.HasPrintedIdentity, "Boundary identity printed");
                Expire(authority);
                await Until(() => clientPreparation.Phase == EntryPhase.Interview, "Fourth Interview started");
                hostInterview.SubmitAnswer("  " + fullLengthName + "  ");
                await Until(() => clientInterview.QuestionIndex == 1, "Outer spaces ignored even for a full-length Unicode answer");

                // Existing offline asset still starts and uses its own local simulation.
                offline = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Taylor/Canvas.prefab"));
                await Task.Delay(100);
                var local = offline.GetComponent<PreparationSession>();
                Check(local.LocalSimulation && local.Phase == EntryPhase.Preparation, "Offline prefab auto-start preserved");
                local.ApplyIdentityEdit(IdentityField.Name, "Local");
                local.ApplyIdentityEdit(IdentityField.Nationality, "LocalLand");
                local.AdvanceLocalTime(100);
                var localInterview = offline.GetComponent<InterviewController>();
                localInterview.EvaluateAnswer("local");
                localInterview.EvaluateAnswer("localland");
                localInterview.EvaluateAnswer("believer");
                Check(localInterview.IsFinished && localInterview.EntryApproved, "Offline answer rules preserved");
                Debug.Log($"COOP INTEGRATION CHECKS PASSED: {checks} checks; real NGO/Unity Transport loopback, 4 online rounds and offline regression. Relay and NGO scene-load synchronization were not exercised by this fixture.");
            }
            catch (Exception exception) { Debug.LogError($"COOP INTEGRATION CHECKS FAILED after {checks} checks: {exception}"); }
            finally
            {
                if (offline != null) UnityEngine.Object.Destroy(offline);
                if (client != null) client.Shutdown();
                if (host != null) host.Shutdown();
                await Task.Delay(300);
                if (authority != null) UnityEngine.Object.Destroy(authority.gameObject);
                if (client != null) UnityEngine.Object.Destroy(client.gameObject);
                host.NetworkConfig.EnableSceneManagement = sceneManagement;
                host.NetworkConfig.ForceSamePrefabs = samePrefabs;
                protocolField.SetValue(hostTransport, protocol);
                if (lobby != null) lobby.SetActive(true);
                running = false;
            }
        }

        private static async Task Same(CoopGameNetworkBridge host, CoopGameNetworkBridge client) =>
            await Until(() => client.CurrentState.Revision == host.CurrentState.Revision, "Round state replicated");

        private static async Task Until(Func<bool> condition, string label)
        {
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (!condition())
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(label);
                await Task.Delay(20);
            }
            Check(true, label);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks++;
        }

        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);

        private static void Expire(CoopGameNetworkBridge bridge)
        {
            var variable = Get<NetworkVariable<CoopGameSnapshot>>(bridge, "snapshot");
            var state = variable.Value;
            state.PreparationEndsAt = bridge.NetworkManager.ServerTime.Time - 1;
            variable.Value = state;
        }

        private static void Replay(CoopGameNetworkBridge bridge, string request, int argument, string text, bool fresh)
            => SendRaw(bridge, bridge.CurrentState.Round, request, argument, text, fresh);

        private static void ReplayRound(CoopGameNetworkBridge bridge, uint round, string request)
            => SendRaw(bridge, round, request, 0, "", true);

        private static void SendRaw(CoopGameNetworkBridge bridge, uint round, string request, int argument, string text, bool fresh)
        {
            ulong id = Get<ulong>(bridge, "nextRequest");
            if (fresh) Set(bridge, "nextRequest", ++id);
            object action = Enum.Parse(typeof(CoopGameNetworkBridge).GetNestedType("Request", BindingFlags.NonPublic), request);
            Call(bridge, "GameplayRequestRpc", round, id, action, argument, NetworkIdentity.Text(text), default(RpcParams));
        }
    }
}
#endif
