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
        public static void Run() => RunChecks(false);

        [MenuItem("Tools/Cooperative Game/Run Loopback Checks With Local Ollama (Play Mode)")]
        public static void RunWithOllama() => RunChecks(true);

        private static async void RunChecks(bool liveOllama)
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
            var rolesPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LobbyRoleSelection.prefab");
            if (rolesPrefab == null) throw new InvalidOperationException("LobbyRoleSelection prefab is required.");
            if (prefab == null) throw new InvalidOperationException("Run Configure Integration Assets first.");
            running = true;
            checks = 0;
            NetworkManager client = null;
            CoopGameNetworkBridge authority = null;
            LobbyRoleSelection roles = null;
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
                if (!host.NetworkConfig.Prefabs.Contains(rolesPrefab)) host.AddNetworkPrefab(rolesPrefab);
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
                client.AddNetworkPrefab(rolesPrefab);
                Check(client.StartClient(), "NGO Client started");
                await Until(() => client.IsConnectedClient && host.ConnectedClientsIds.Count == 2, "Host/Client connected");
                if (lobby != null) lobby.SetActive(false);
                roles = UnityEngine.Object.Instantiate(rolesPrefab).GetComponent<LobbyRoleSelection>();
                roles.GetComponent<NetworkObject>().Spawn();
                authority = UnityEngine.Object.Instantiate(prefab).GetComponent<CoopGameNetworkBridge>();
                authority.GetComponent<NetworkObject>().Spawn();
                CoopGameNetworkBridge remote = null;
                await Until(() =>
                {
                    foreach (var bridge in UnityEngine.Object.FindObjectsByType<CoopGameNetworkBridge>())
                        if (bridge.IsSpawned && bridge.NetworkManager == client) remote = bridge;
                    return remote != null && remote.CurrentState.Revision != 0;
                }, "Bridge spawned and initial snapshot replicated");
                Set(authority.GetComponentInChildren<InterviewController>(true), "useOllama", liveOllama);
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
                await Until(() => Mathf.Abs(hostPreparation.RemainingSeconds - clientPreparation.RemainingSeconds) < 0.3f,
                    "Countdown derives from a shared server deadline");

                hostPreparation.RequestIdentityEdit(IdentityField.Name, "Unauthorized");
                await Task.Delay(150);
                Check(string.IsNullOrEmpty(hostPreparation.Identity.Name), "Traveler identity edits rejected");
                hostPreparation.RequestGovernmentEdit(GovernmentField.Name, "Unauthorized");
                await Task.Delay(100);
                Check(string.IsNullOrEmpty(hostPreparation.Government.Name), "Traveler government edits rejected");
                var governmentView = remote.GetComponentInChildren<PreparationPreview>(true);
                var governmentEditor = Get<GameObject>(governmentView, "hacker").GetComponent<GovernmentDocumentView>();
                Get<TMPro.TMP_InputField>(governmentEditor, "nameInput").text = "Alice";
                Get<TMPro.TMP_InputField>(governmentEditor, "nationalityInput").text = "Enlaslov";
                Get<TMPro.TMP_InputField>(governmentEditor, "positionInput").text = "Cultural Attache";
                await Until(() => hostPreparation.Government.Position == "Cultural Attache", "Government text replicated");
                clientPreparation.RequestGovernmentPrint();
                clientPreparation.RequestGovernmentEdit(GovernmentField.Category, "99");
                await Task.Delay(100);
                Check(!hostPreparation.HasPrintedGovernment && hostPreparation.Government.Category == TaskCategory.None,
                    "Missing/invalid category cannot print");
                Get<UnityEngine.UI.Button[]>(governmentEditor, "categoryButtons")[1].onClick.Invoke();
                // Enter immediately after selecting: must wait for the category acknowledgement.
                Get<TMPro.TMP_InputField>(governmentEditor, "positionInput").onSubmit.Invoke("Cultural Attache");
                await Until(() => hostPreparation.HasPrintedGovernment && clientPreparation.HasPrintedGovernment,
                    "Government print replicated");
                Check(hostPreparation.PrintedGovernment.Category == TaskCategory.CulturalConnection &&
                    clientPreparation.PrintedGovernment.Position == "Cultural Attache", "Printed category and text preserved");
                Check(!hostPreparation.HasPrintedIdentity && !Get<TMPro.TMP_InputField>(governmentEditor, "nameInput").interactable,
                    "Government locks independently of passport");
                var travelerGovernment = Get<GameObject>(authority.GetComponentInChildren<PreparationPreview>(true), "traveler")
                    .GetComponent<GovernmentDocumentView>();
                Check(Get<GameObject>(travelerGovernment, "document").activeSelf &&
                    Get<UnityEngine.UI.Image[]>(travelerGovernment, "categoryCircles")[1].gameObject.activeSelf,
                    "Traveler sees printed government document and circle");
                clientPreparation.RequestGovernmentEdit(GovernmentField.Name, "After print");
                await Task.Delay(100);
                Check(hostPreparation.Government.Name == "Alice", "Government edits rejected after print");
                var hackerView = remote.transform.GetComponentInChildren<PreparationPreview>(true);
                var hackerPanel = Get<GameObject>(hackerView, "hacker");
                var printView = hackerPanel.GetComponent<PreparationView>();
                var nameField = Get<TMPro.TMP_InputField>(printView, "nameInput");
                nameField.onSubmit.Invoke("");
                await Task.Delay(100);
                Check(!hostPreparation.HasPrintedIdentity && nameField.isFocused &&
                    nameField.GetComponent<UnityEngine.UI.Outline>().enabled,
                    "Enter on incomplete document highlights and focuses missing field");
                nameField.text = "Alice";
                Get<TMPro.TMP_InputField>(printView, "birthPlaceInput").text = "Enlaslov";
                Check(!Get<TMPro.TMP_InputField>(printView, "issueDateInput").gameObject.activeSelf &&
                    !Get<TMPro.TMP_InputField>(printView, "expiryDateInput").gameObject.activeSelf,
                    "Fixed passport dates have no editable UI");
                Get<TMPro.TMP_InputField>(printView, "birthdayInput").text = "1950-04-12";
                Get<TMPro.TMP_InputField>(printView, "passportCodeInput").text = "ID-42";
                // Submit from the first field immediately, while the final edits are still in flight.
                nameField.onSubmit.Invoke(nameField.text);
                await Until(() => hostPreparation.HasPrintedIdentity && clientPreparation.HasPrintedIdentity, "Print replicated");
                Check(hostPreparation.PrintedIdentity.Name == "Alice", "Printed identity matches current identity");
                Check(hostPreparation.PrintedIdentity.Birthday == "1950-04-12" &&
                    clientPreparation.PrintedIdentity.Birthday == "1950-04-12", "Birthday printed and replicated");
                Check(hostPreparation.PrintedIdentity.IssueDate == IdentityCard.FixedIssueDate &&
                    hostPreparation.PrintedIdentity.ExpiryDate == IdentityCard.FixedExpiryDate &&
                    clientPreparation.PrintedIdentity.ExpiryDate == IdentityCard.FixedExpiryDate, "Fixed passport dates survive printing and replication");
                Check(hostPreparation.PrintedIdentity.PassportCode == "ID-42" && !nameField.interactable &&
                    Get<GameObject>(printView, "printedStamp").activeSelf, "Enter waits for final edit, locks document and displays stamp");
                clientPreparation.RequestIdentityEdit(IdentityField.Name, "Changed after print");
                clientPreparation.RequestPrint();
                await Task.Delay(150);
                Check(hostPreparation.Identity.Name == "Alice", "Printed identity locked and repeat Print ignored");
                Expire(authority);
                await Until(() => clientPreparation.Phase == EntryPhase.Interview, "Interview phase replicated atomically with printed identity");
                Check(hostInterview.IsShowingIntroduction && clientInterview.IsShowingIntroduction &&
                    !hostInterview.QuestionTimerRunning && !clientInterview.QuestionTimerRunning,
                    "Introduction shown on both peers without question countdown");
                Check(!hostInterview.EvaluateAnswer("Alice") && hostInterview.QuestionIndex == 0,
                    "Answers cannot bypass introduction");
                Check(Math.Abs(hostInterview.IntroductionEndsAt - clientInterview.IntroductionEndsAt) < .001,
                    "Introduction deadline synchronized");
                await Until(() => !hostInterview.IsShowingIntroduction && !clientInterview.IsShowingIntroduction,
                    "Introduction automatically ends on both peers");
                Check(hostInterview.QuestionSecondsRemaining > 29 && clientInterview.CurrentQuestion == "What is your name, sir?",
                    "First question starts with full allowance");
                Check(clientPreparation.PrintedIdentity.Name == "Alice" && clientInterview.Reputation == 50, "Interview starts with correct identity and reputation");
                Check(hostInterview.QuestionEndsAt > host.ServerTime.Time &&
                    Math.Abs(hostInterview.QuestionEndsAt - clientInterview.QuestionEndsAt) < .001,
                    "Question deadline is replicated to both peers");
                Check(!clientInterview.EvaluateAnswer("Alice") && !clientInterview.ApplyShake(),
                    "Remote instance cannot run authority-only evaluation or reputation changes");
                Check(Get<GameObject>(remote.GetComponentInChildren<PreparationPreview>(true), "hackerInterview").activeSelf,
                    "Hacker interview desktop visible");
                hostInterview.SubmitAnswer("Hello officer, my name is ALICE.");
                await Until(() => clientInterview.QuestionIndex == 1, "Case/whitespace-insensitive answer advances both peers", 65);
                Replay(authority, "Answer", 0, "Alice", false);
                await Task.Delay(150);
                Check(hostInterview.QuestionIndex == 1, "Duplicate answer request ignored");
                Replay(authority, "Answer", 0, "Alice", true);
                await Task.Delay(150);
                Check(hostInterview.QuestionIndex == 1 && !hostInterview.IsFinished, "Stale question index rejected");
                Replay(remote, "Answer", 1, "Canada", true);
                await Task.Delay(150);
                Check(hostInterview.QuestionIndex == 1, "Hacker cannot submit Traveler answers");
                hostInterview.SubmitAnswer("I am coming from Enlaslov.");
                await Until(() => clientInterview.QuestionIndex == 2, "Second answer replicated", 65);
                hostInterview.SubmitAnswer("My trip is for cultural connection.");
                await Until(() => clientInterview.QuestionIndex == 3, "Task category sentence replicated", 65);
                Check(clientInterview.CurrentQuestion == hostInterview.CurrentQuestion, "Final wording matches both screens");
                if (liveOllama)
                {
                    hostInterview.SubmitAnswer("Purple chairs sing bananas.");
                    await Until(() => clientInterview.ReviewingPurpose, "Pending review replicated");
                    Check(!clientInterview.QuestionTimerRunning, "Clock paused during review");
                    await Until(() => !clientInterview.ReviewingPurpose, "Unrelated answer reviewed", 65);
                    Check(!clientInterview.IsFinished && clientInterview.QuestionIndex == 3, "Unrelated answer can retry");
                }
                hostInterview.SubmitAnswer("Until my work here is done.");
                await Until(() => clientInterview.IsFinished, "Victory replicated", 65);
                Check(hostInterview.EntryApproved && clientInterview.EntryApproved, "Both peers approve entry");
                Call(remote, "ApplySnapshot", remote.CurrentState);
                Call(remote, "ApplySnapshot", remote.CurrentState);
                Check(hostFinished == 1 && clientFinished == 1, "Duplicate final snapshots do not repeat Finished callbacks");
                Check(Get<GameObject>(clientInterview, "gameOverPanel").activeSelf, "Remote GameOver UI displayed");

                // Subsequent rounds exercise the deterministic fallback rules.
                Set(hostInterview, "useOllama", false);
                Set(clientInterview, "useOllama", false);
                authority.StartNewRound();
                await Until(() => remote.CurrentState.Round == 2, "Second round replicated");
                Check(!clientInterview.IsFinished && clientInterview.QuestionIndex == 0 && clientInterview.Reputation == 50 &&
                    !clientPreparation.HasPrintedIdentity && !clientPreparation.Identity.IsComplete, "New round resets identity, print, progress, result and reputation");
                uint round = authority.CurrentState.Round;
                authority.StartNewRound();
                Check(authority.CurrentState.Round == round, "Active round cannot start twice");
                Expire(authority);
                await Until(() => clientPreparation.Phase == EntryPhase.Interview, "Second Interview started without inventing a missing-print penalty");
                await Until(() => !clientInterview.IsShowingIntroduction, "Second introduction ended");
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
                await Until(() => !clientInterview.IsShowingIntroduction, "Third introduction ended");
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
                clientPreparation.RequestIdentityEdit(IdentityField.BirthPlace, "Canada");
                clientPreparation.RequestIdentityEdit(IdentityField.IssueDate, "1977-11-02");
                clientPreparation.RequestIdentityEdit(IdentityField.ExpiryDate, "1979-11-02");
                clientPreparation.RequestIdentityEdit(IdentityField.Birthday, "1950-04-12");
                clientPreparation.RequestIdentityEdit(IdentityField.PassportCode, "ID-42");
                await Until(() => clientPreparation.CanPrint, "64-character Unicode identity transported intact");
                clientPreparation.RequestPrint();
                await Until(() => hostPreparation.HasPrintedIdentity, "Boundary identity printed");
                Expire(authority);
                await Until(() => clientPreparation.Phase == EntryPhase.Interview, "Fourth Interview started");
                await Until(() => !clientInterview.IsShowingIntroduction, "Fourth introduction ended");
                hostInterview.SubmitAnswer("  " + fullLengthName + "  ");
                await Until(() => clientInterview.QuestionIndex == 1, "Outer spaces ignored even for a full-length Unicode answer");

                hostInterview.SetNetworkClock(() => hostInterview.QuestionEndsAt + 1);
                await Until(() => clientInterview.IsFinished, "Question timeout failure replicated");
                Check(!clientInterview.EntryApproved && clientInterview.ResultReason.Contains("in time"),
                    "Timeout uses shared entry-denied result");

                // Exercise the new lobby selection: the host can be Hacker.
                authority.GetComponent<NetworkObject>().Despawn();
                await Until(() => remote == null || !remote.IsSpawned, "Old game despawned");
                var roleVariable = Get<NetworkVariable<LobbyRoleState>>(roles, "state");
                var selected = roleVariable.Value;
                selected.Host.Role = LobbyRole.Hacker;
                selected.Client.Role = LobbyRole.Traveler;
                roleVariable.Value = selected;
                authority = UnityEngine.Object.Instantiate(prefab).GetComponent<CoopGameNetworkBridge>();
                authority.GetComponent<NetworkObject>().Spawn();
                remote = null;
                await Until(() =>
                {
                    foreach (var bridge in UnityEngine.Object.FindObjectsByType<CoopGameNetworkBridge>())
                        if (bridge.IsSpawned && bridge.NetworkManager == client) remote = bridge;
                    return remote != null && remote.CurrentState.Revision != 0;
                }, "Swapped-role game replicated");
                Set(authority.GetComponentInChildren<InterviewController>(true), "useOllama", false);
                Set(authority, "ready", true);
                authority.StartNewRound();
                await Same(authority, remote);
                Check(authority.LocalRole == PlayerRole.Hacker && remote.LocalRole == PlayerRole.Traveler &&
                    authority.CurrentState.HackerId == host.LocalClientId &&
                    authority.CurrentState.TravelerId == client.LocalClientId, "Selected roles override host/client defaults");
                var swappedHostPreparation = authority.GetComponentInChildren<PreparationSession>(true);
                swappedHostPreparation.RequestIdentityEdit(IdentityField.Name, "HostHacker");
                await Until(() => remote.GetComponentInChildren<PreparationSession>(true).Identity.Name == "HostHacker",
                    "Host Hacker can forge identity");
                Expire(authority);
                await Until(() => remote.CurrentState.Phase == EntryPhase.Interview, "Swapped-role interview started");
                await Until(() => !remote.GetComponentInChildren<InterviewController>(true).IsShowingIntroduction,
                    "Swapped-role introduction ended");
                remote.GetComponentInChildren<InterviewController>(true).SubmitAnswer("HostHacker");
                await Until(() => authority.CurrentState.QuestionIndex == 1 &&
                    remote.CurrentState.QuestionIndex == 1, "Client Traveler can answer");

                remote.GetComponentInChildren<InterviewController>(true).SubmitAnswer("Wrong nationality");
                await Until(() => authority.CurrentState.IsFinished, "Finish swapped-role round for mismatch test");
                authority.StartNewRound();
                await Same(authority, remote);
                swappedHostPreparation.RequestIdentityEdit(IdentityField.Name, "Alice");
                swappedHostPreparation.RequestIdentityEdit(IdentityField.BirthPlace, "Enlaslov");
                swappedHostPreparation.RequestIdentityEdit(IdentityField.Birthday, "1950-04-12");
                swappedHostPreparation.RequestIdentityEdit(IdentityField.IssueDate, "1977-11-02");
                swappedHostPreparation.RequestIdentityEdit(IdentityField.ExpiryDate, "1979-11-02");
                swappedHostPreparation.RequestIdentityEdit(IdentityField.PassportCode, "XYZ0000");
                swappedHostPreparation.RequestPrint();
                swappedHostPreparation.RequestGovernmentEdit(GovernmentField.Name, "Alice");
                swappedHostPreparation.RequestGovernmentEdit(GovernmentField.Nationality, "Different Country");
                swappedHostPreparation.RequestGovernmentEdit(GovernmentField.Position, "Diplomat");
                swappedHostPreparation.RequestGovernmentEdit(GovernmentField.Category, "1");
                swappedHostPreparation.RequestGovernmentPrint();
                await Until(() => remote.CurrentState.HasPrintedGovernment && remote.CurrentState.HasPrintedIdentity,
                    "Both conflicting printed documents replicated");
                Expire(authority);
                await Until(() => remote.CurrentState.DocumentMismatch, "Mismatch flag replicated to Traveler");
                var challenged = remote.GetComponentInChildren<InterviewController>(true);
                await Until(() => !challenged.IsShowingIntroduction, "Mismatch introduction ended");
                Check(challenged.CurrentQuestion.Contains("discrepancy") && !challenged.IsFinished,
                    "Contradiction question shown before failure");
                challenged.SubmitAnswer("My name is Alice.");
                await Until(() => authority.CurrentState.IsFinished && remote.CurrentState.IsFinished,
                    "Any answer to contradiction fails both peers");
                Check(!remote.CurrentState.EntryApproved && remote.CurrentState.ResultReason.Contains("does not match"),
                    "Correct passport name cannot bypass document mismatch");

                // Existing offline asset still starts and uses its own local simulation.
                offline = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Taylor/Canvas.prefab"));
                await Task.Delay(100);
                var local = offline.GetComponent<PreparationSession>();
                Check(local.LocalSimulation && local.Phase == EntryPhase.Preparation, "Offline prefab auto-start preserved");
                local.ApplyIdentityEdit(IdentityField.Name, "Local");
                local.ApplyIdentityEdit(IdentityField.BirthPlace, "LocalLand");
                local.ApplyGovernmentEdit(GovernmentField.Nationality, "LocalLand");
                local.ApplyGovernmentEdit(GovernmentField.Position, "Diplomat");
                local.ApplyGovernmentEdit(GovernmentField.Category, "1");
                local.AdvanceLocalTime(100);
                var localInterview = offline.GetComponent<InterviewController>();
                Set(localInterview, "useOllama", false); // Offline regression covers the deterministic option.
                await Until(() => !localInterview.IsShowingIntroduction, "Offline introduction ended");
                localInterview.EvaluateAnswer("local");
                await Until(() => localInterview.QuestionIndex == 1, "Local name reviewed", 65);
                localInterview.EvaluateAnswer("localland");
                await Until(() => localInterview.QuestionIndex == 2, "Local origin reviewed", 65);
                localInterview.EvaluateAnswer("My task concerns politics.");
                await Until(() => localInterview.QuestionIndex == 3, "Local purpose reviewed", 65);
                localInterview.EvaluateAnswer("A few days.");
                await Until(() => localInterview.IsFinished, "Offline final answer reviewed", 65);
                Check(localInterview.IsFinished && localInterview.EntryApproved, "Offline answer rules preserved");
                Debug.Log($"COOP INTEGRATION CHECKS PASSED: {checks} checks; real NGO/Unity Transport loopback, timeout, swapped roles and offline regression. Relay and NGO scene-load synchronization were not exercised by this fixture.");
            }
            catch (Exception exception) { Debug.LogError($"COOP INTEGRATION CHECKS FAILED after {checks} checks: {exception}"); }
            finally
            {
                if (offline != null) UnityEngine.Object.Destroy(offline);
                if (client != null) client.Shutdown();
                if (host != null) host.Shutdown();
                await Task.Delay(300);
                if (authority != null) UnityEngine.Object.Destroy(authority.gameObject);
                if (roles != null) UnityEngine.Object.Destroy(roles.gameObject);
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

        private static async Task Until(Func<bool> condition, string label, int timeoutSeconds = 10)
        {
            double deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
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
            Call(bridge, "GameplayRequestRpc", round, id, action, argument, NetworkIdentity.Text(text), bridge.CurrentState.QuestionEndsAt, default(RpcParams));
        }
    }
}
#endif
