using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CarryOn.Taylor;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Multiplayer
{
    // Lives alongside the persistent room manager, across lobby and game scenes.
    public sealed class OllamaRoomLifetime : MonoBehaviour
    {
        [SerializeField] private InterviewController interviewSettings;
        private NetworkSessionManager session;
        private string loadedModel;
        private Task pending;

        private void Awake() => session = GetComponent<NetworkSessionManager>();

        private void Update()
        {
            if (session == null || (pending != null && !pending.IsCompleted)) return;
            if (loadedModel != null && !session.IsHost)
            {
                string model = loadedModel;
                loadedModel = null;
                pending = SetResident(model, false);
            }
            else if (loadedModel == null && session.IsHost && session.Players.Count == 2 &&
                     interviewSettings != null && interviewSettings.UsesOllama)
            {
                loadedModel = interviewSettings.OllamaModel;
                pending = SetResident(loadedModel, true);
            }
        }

        private static async Task SetResident(string model, bool resident)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                var body = new { model, stream = false, keep_alive = resident ? -1 : 0,
                    options = new { num_ctx = 2048, num_thread = 2 } };
                using var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync("http://127.0.0.1:11434/api/generate", content);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception error) { Debug.LogWarning("Ollama room model: " + error.Message); }
        }

        private async void OnDestroy()
        {
            string model = loadedModel;
            loadedModel = null;
            if (pending != null) await pending;
            if (model != null) await SetResident(model, false);
        }
    }
}
