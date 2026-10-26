#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Threading.Tasks;
using CarryOn.Taylor;
using Unity.Services.Core;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Multiplayer
{
    // Opt-in development-player check. Normal editor/player launches never attach this component.
    public sealed class CoopGameRelayCheckClient : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AttachIfRequested()
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("--coop-check-code=", StringComparison.Ordinal))
                    new GameObject("RelayCheckClient").AddComponent<CoopGameRelayCheckClient>();
        }

        private async void Start()
        {
            DontDestroyOnLoad(gameObject);
            string code = Argument("--coop-check-code=");
            string report = Argument("--coop-check-report=");
            try
            {
                await UnityServices.InitializeAsync(new InitializationOptions().SetProfile("coopcheck" + Guid.NewGuid().ToString("N").Substring(0, 16)));
                var session = FindAnyObjectByType<NetworkSessionManager>();
                session.JoinRoom(code);
                await Until(() => session.State == ConnectionState.Connected, "Relay Client connection", 45);
                CoopGameNetworkBridge bridge = null;
                await Until(() =>
                {
                    bridge = FindAnyObjectByType<CoopGameNetworkBridge>();
                    return bridge != null && bridge.IsSpawned && bridge.CurrentState.Round == 1;
                }, "NGO GameScene load", 45);
                Require(bridge.LocalRole == PlayerRole.Hacker, "Client role must be Hacker");
                Require(bridge.GetComponentsInChildren<Canvas>(true).Length == 1, "Exactly one gameplay Canvas");
                Require(FindObjectsByType<EventSystem>().Length == 1, "Exactly one EventSystem");
                var preparation = bridge.GetComponentInChildren<PreparationSession>(true);
                var interview = bridge.GetComponentInChildren<InterviewController>(true);
                int helps = 0, finished = 0;
                bridge.HelpRequested += () => helps++;
                interview.Finished += () => finished++;
                preparation.RequestIdentityEdit(IdentityField.Name, "Relay Alice");
                preparation.RequestIdentityEdit(IdentityField.Nationality, "Canada");
                preparation.RequestIdentityEdit(IdentityField.Age, "28");
                preparation.RequestIdentityEdit(IdentityField.LicenseNumber, "RELAY-42");
                await Until(() => preparation.CanPrint, "Identity echo and CanPrint");
                preparation.RequestPrint();
                await Until(() => preparation.HasPrintedIdentity, "Print echo");
                File.WriteAllText(report, "printed");
                await Until(() => interview.IsFinished, "Replicated victory", 90);
                Require(interview.EntryApproved && interview.Reputation == 30 && helps == 1 && finished == 1,
                    "Expected one help event, reputation 30 and one approved result");
                Require(preparation.PrintedIdentity.Name == "Relay Alice", "Printed identity retained at result");
                File.WriteAllText(report, "victory");
                await Until(() => !session.IsInGame && bridge == null, "NGO return to Lobby", 30);
                File.WriteAllText(report, "lobby");
                await Until(() => session.State != ConnectionState.Connected, "Host disconnect notification", 30);
                await Task.Delay(300);
                Require(FindAnyObjectByType<CoopGameNetworkBridge>() == null, "No stale gameplay object after shutdown");
                File.WriteAllText(report, "passed");
                Debug.Log("COOP RELAY CLIENT CHECK PASSED");
            }
            catch (Exception error)
            {
                Debug.LogError(error);
                File.WriteAllText(report, "failed: " + error);
            }
            finally { Application.Quit(); }
        }

        private static string Argument(string prefix)
        {
            foreach (string value in Environment.GetCommandLineArgs())
                if (value.StartsWith(prefix, StringComparison.Ordinal)) return value.Substring(prefix.Length);
            return string.Empty;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static async Task Until(Func<bool> condition, string message, int seconds = 20)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException(message);
                await Task.Delay(20);
            }
        }
    }
}
#endif
