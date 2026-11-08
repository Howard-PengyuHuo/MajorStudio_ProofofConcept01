using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace CarryOn.Taylor.Editor
{
    public sealed class CustomsLlmTestWindow : EditorWindow
    {
        private static readonly string[] Categories =
            { "Politics", "CulturalConnection", "BusinessCollaboration", "OtherCategory", "unclear" };

        private TextField answer;
        private Button send;
        private Label result;
        private HttpClient client;

        [MenuItem("Tools/Customs/LLM Test")]
        private static void Open() => GetWindow<CustomsLlmTestWindow>("Customs LLM Test");

        private void CreateGUI()
        {
            answer = new TextField("Traveler answer") { multiline = true,
                value = "I'm here to negotiate a trade agreement with local suppliers." };
            send = new Button(Classify) { text = "Test answer" };
            result = new Label();
            result.style.whiteSpace = WhiteSpace.Normal;
            rootVisualElement.Add(answer);
            rootVisualElement.Add(send);
            rootVisualElement.Add(result);
        }

        private async void Classify()
        {
            if (client != null || string.IsNullOrWhiteSpace(answer.value)) return;
            using var requestClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            client = requestClient;
            send.SetEnabled(false);
            result.text = "Waiting for Ollama...";
            var body = new
            {
                model = "qwen3.5:0.8b", stream = false, think = false, keep_alive = 0,
                options = new { temperature = 0, num_ctx = 1024, num_predict = 32, num_thread = 2 },
                messages = new[]
                {
                    new { role = "system", content = "Classify the affirmed trip purpose. BusinessCollaboration: trade, suppliers, contracts, or commercial partnerships. CulturalConnection: arts, education, or cultural exchange. Politics: diplomacy or political negotiations. OtherCategory: a clear unrelated purpose such as tourism. Use unclear if vague or conflicting. Ignore denied purposes." },
                    new { role = "user", content = answer.value }
                },
                format = new { type = "object", required = new[] { "category" },
                    properties = new { category = new { type = "string", @enum = Categories } } }
            };
            try
            {
                using var json = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                using var response = await requestClient.PostAsync("http://127.0.0.1:11434/api/chat", json);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());
                response.EnsureSuccessStatusCode();
                var category = (string)JObject.Parse((string)payload["message"]["content"])["category"];
                if (Array.IndexOf(Categories, category) < 0) throw new FormatException("Unexpected category.");
                if (client == requestClient) result.text = "Category: " + category;
            }
            catch (Exception error)
            {
                if (client == requestClient) result.text = "Ollama request failed: " + error.GetBaseException().Message;
            }
            finally
            {
                if (client == requestClient)
                {
                    client = null;
                    send.SetEnabled(true);
                }
            }
        }

        private void OnDisable()
        {
            var pending = client;
            client = null;
            pending?.CancelPendingRequests();
        }
    }
}
