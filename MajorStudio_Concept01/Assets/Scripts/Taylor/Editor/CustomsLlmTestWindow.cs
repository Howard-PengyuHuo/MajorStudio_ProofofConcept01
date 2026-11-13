using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace CarryOn.Taylor.Editor
{
    public sealed class CustomsLlmTestWindow : EditorWindow
    {
        private const string SettingsPath = "Assets/Prefabs/Taylor/Canvas.prefab";
        private InterviewController settings;
        private DropdownField topic;
        private TextField nameInput, nationality, purpose, governmentRole, answer, question, transcript, prompt, raw;
        private IReadOnlyList<InterviewQuestion> testQuestions;
        private InterviewQuestion extensionQuestion;
        private string followupParentAnswer = "";
        private int followupsAsked;
        private InterviewQuestion ActiveQuestion => extensionQuestion ?? testQuestions[topic.index];
        private Label status;
        private VisualElement controls;
        private Button submit, next, cancel;
        private readonly JArray history = new JArray();
        private CancellationTokenSource cancellation;
        private bool accepted, denied, closed;
        public bool Busy => cancellation != null;
        public string LastResult { get; private set; }
        public string LastModel { get; private set; }
        public string CurrentLine => question?.value;

        [MenuItem("Tools/Customs/LLM Test")]
        public static void Open() => GetWindow<CustomsLlmTestWindow>("Customs LLM Test");

        private TextField Field(VisualElement parent, string label, string value = "", bool readOnly = false)
        {
            var field = new TextField(label) { value = value, isReadOnly = readOnly, multiline = readOnly };
            field.name = label;
            field.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(field);
            return field;
        }

        public void CreateGUI()
        {
            closed = false;
            rootVisualElement.Clear();
            minSize = new UnityEngine.Vector2(650, 650);
            var scroll = new ScrollView(); rootVisualElement.Add(scroll);
            scroll.Add(new HelpBox("Uses the game's saved Canvas configuration, full prompt, JSON schema and reply parser. No Play Mode or multiplayer required. This tests AI dialogue, not game timers or reputation.", HelpBoxMessageType.Info));
            status = new Label(); status.style.whiteSpace = WhiteSpace.Normal; scroll.Add(status);
            controls = new VisualElement(); scroll.Add(controls);
            controls.Add(new Button(Reload) { text = "Reload settings / new random interview" });
            controls.Add(new Button(() => Selection.activeObject = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(SettingsPath)) { text = "Select game Canvas settings" });
            nameInput = Field(controls, "Document name", "ALICE SMITH");
            nationality = Field(controls, "Nationality", "GRANEYA");
            purpose = Field(controls, "Purpose category", "Cultural Connection");
            governmentRole = Field(controls, "Government role", "OFFICER");
            topic = new DropdownField("Question"); controls.Add(topic);
            topic.RegisterValueChangedCallback(_ => ResetConversation());
            controls.Add(new Button(async () => await GenerateQuestionAsync()) { text = "Generate / rephrase this question" });
            question = Field(controls, "Officer question", "", true);
            answer = Field(controls, "Player answer"); answer.multiline = true;
            submit = new Button(async () => await ReviewAnswerAsync(answer.value)) { text = "Submit answer" }; controls.Add(submit);
            next = new Button(async () =>
            {
                var previousHistory = (JArray)history.DeepClone();
                string previousTranscript = transcript.value, previousRaw = raw.value;
                topic.index++;
                foreach (var turn in previousHistory) history.Add(turn);
                transcript.value = previousTranscript;
                raw.value = previousRaw;
                await GenerateQuestionAsync();
            }) { text = "Next question (after acceptance)" }; controls.Add(next);
            cancel = new Button(() => cancellation?.Cancel()) { text = "Cancel request" }; scroll.Add(cancel); cancel.SetEnabled(false);
            transcript = Field(scroll, "Conversation and results", "", true);
            var promptFold = new Foldout { text = "Actual complete system prompt (Main Prompt + fixed rules + security rules)", value = false }; scroll.Add(promptFold);
            prompt = Field(promptFold, "", "", true);
            var rawFold = new Foldout { text = "Raw Ollama responses", value = false }; scroll.Add(rawFold);
            raw = Field(rawFold, "", "", true);
            Reload();
        }

        private void Reload()
        {
            followupsAsked = 0;
            settings = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(SettingsPath)?.GetComponent<InterviewController>();
            testQuestions = InterviewController.BuildInterviewPlan(Guid.NewGuid().GetHashCode());
            if (settings == null || testQuestions == null || testQuestions.Count == 0)
            { status.text = "Missing game Canvas / InterviewController questions."; controls.SetEnabled(false); return; }
            var choices = new List<string>();
            for (int i = 0; i < testQuestions.Count; i++) choices.Add($"{i + 1}. {testQuestions[i].topic}");
            topic.choices = choices; topic.SetValueWithoutNotify(choices[0]);
            prompt.value = (string)settings.BuildOllamaPayload("{}", true)["messages"][0]["content"];
            status.text = $"Game + test model: {settings.OllamaModel}\nSettings: {SettingsPath}";
            ResetConversation();
        }

        private void ResetConversation()
        {
            if (settings == null || question == null) return;
            history.Clear(); accepted = denied = false; LastResult = ""; LastModel = "";
            extensionQuestion = null;
            followupParentAnswer = "";
            question.value = testQuestions[topic.index].question.ToUpperInvariant();
            transcript.value = ""; raw.value = ""; answer.value = "";
            RefreshButtons();
        }

        private void RefreshButtons()
        {
            submit?.SetEnabled(!accepted && !denied);
            next?.SetEnabled(accepted && topic.index + 1 < testQuestions.Count);
        }

        private object References(InterviewTopic subject) => subject switch
        {
            InterviewTopic.PromisedDignity => InterviewController.ReligiousReferences(),
            InterviewTopic.HeavenlyFatherName => InterviewController.ReligiousReferences(),
            InterviewTopic.DivineRetribution => InterviewController.ReligiousReferences(),
            InterviewTopic.Name => new { name = nameInput.value },
            InterviewTopic.Nationality => new { nationality = nationality.value },
            InterviewTopic.VisitPurpose => new { purpose_category = purpose.value },
            InterviewTopic.GovernmentRole => new { government_role = governmentRole.value },
            InterviewTopic.RoleDuties => new { government_role = governmentRole.value },
            _ => new { }
        };

        public Task GenerateQuestionAsync() => Run(async () =>
        {
            if (denied) throw new InvalidOperationException("Entry denied. Reset the conversation to start a new test.");
            var q = ActiveQuestion;
            var reply = await Ask(new { action = "generate_question", round = topic.index + 1,
                total_rounds = testQuestions.Count, current_topic = q.topic.ToString(),
                original_question = q.question, documents = References(q.topic), history, parent_answer = followupParentAnswer, variation = Guid.NewGuid().ToString("N") }, true);
            question.value = InterviewController.MatchesTopic(reply.Line, q.topic) && Fresh(reply.Line)
                ? reply.Line : q.question.ToUpperInvariant();
            if (extensionQuestion != null) extensionQuestion.question = question.value;
            accepted = false;
            LastResult = "question";
            transcript.value += $"\nOFFICER: {question.value}\n";
        });

        private bool Fresh(string line)
        {
            foreach (var turn in history)
                if (string.Equals((string)turn["question"], line, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public Task ReviewAnswerAsync(string value) => Run(async () =>
        {
            if (denied) throw new InvalidOperationException("Entry denied. Reset the conversation to start a new test.");
            if (accepted) throw new InvalidOperationException("This question passed. Select Next question or reset.");
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Enter an answer first.");
            var q = ActiveQuestion;
            string current = question.value;
            // Gameplay input is uppercase too.
            value = value.Trim().ToUpperInvariant();
            var documents = References(q.topic);
            var reply = await Ask(new { action = "review_answer", current_topic = q.topic.ToString(),
                question = current, original_question = q.question, documents, answer = value, history, parent_answer = followupParentAnswer }, false);
            if (followupsAsked < settings.MaximumFollowups && reply.Verdict == "accept" && InterviewController.CreateFollowup(q.topic) != null)
            {
                var coverage = await Ask(InterviewController.FollowupCoverageData(q.topic, value, documents), false);
                reply.FollowupComplete = InterviewController.HasFollowupEvidence(value, coverage);
            }
            history.Add(new JObject { ["question"] = current, ["answer"] = value, ["intent"] = reply.Intent, ["verdict"] = reply.Verdict });
            while (history.Count > 12) history.RemoveAt(0);
            LastResult = $"{reply.Verdict} / {reply.Intent} / {reply.Reason}";
            transcript.value += $"\nPLAYER: {value}\nRESULT: {LastResult}\n";
            if (reply.Verdict == "deny")
            {
                denied = true; accepted = false;
                question.value = InterviewController.RefusalReason(reply);
                transcript.value += question.value + "\n[Game displays this reason before the ending.]\n";
                return;
            }
            if (followupsAsked < settings.MaximumFollowups && InterviewController.NeedsFollowup(q.topic, reply))
            {
                followupsAsked++;
                followupParentAnswer = value;
                extensionQuestion = InterviewController.CreateFollowup(q.topic);
                question.value = extensionQuestion.question.ToUpperInvariant();
                var followup = await Ask(new { action = "generate_question", current_topic = extensionQuestion.topic.ToString(),
                    original_question = extensionQuestion.question, documents = References(extensionQuestion.topic), history, parent_answer = followupParentAnswer }, true);
                question.value = Fresh(followup.Line) && InterviewController.MatchesTopic(followup.Line, extensionQuestion.topic)
                    ? followup.Line : extensionQuestion.question.ToUpperInvariant();
                extensionQuestion.question = question.value;
                transcript.value += $"OFFICER: {question.value}\n";
                return;
            }
            if (reply.Verdict == "accept") { accepted = true; transcript.value += "PASSED — Next question is available.\n"; return; }
            string help = InterviewController.SupportedHelpReply(reply.Intent, q.question, q.topic);
            if (help == null && (!Fresh(reply.Line) || !InterviewController.MatchesTopic(reply.Line, q.topic)))
            {
                var repaired = await Ask(new { action = "generate_question", current_topic = q.topic.ToString(), player_intent = reply.Intent,
                    instruction = "RESPOND TO THE PLAYER'S INTENT: REPHRASE MEANS SIMPLIFY THE SAME QUESTION; EXPLAIN MEANS ANSWER THEIR REQUEST THEN REASK SIMPLY. OTHERWISE SEEK THE MISSING DETAIL. USE NEW WORDING. DO NOT CHANGE SUBJECT.",
                    question = current, original_question = q.question, answer = value, history, documents, parent_answer = followupParentAnswer }, true);
                reply.Line = repaired.Line;
            }
            question.value = help ?? (Fresh(reply.Line) && InterviewController.MatchesTopic(reply.Line, q.topic)
                ? reply.Line : q.question.ToUpperInvariant());
            transcript.value += $"OFFICER: {question.value}\n";
        });

        private async Task<InterviewController.OfficerReply> Ask(object data, bool generating)
        {
            var payload = settings.BuildOllamaPayload(Newtonsoft.Json.JsonConvert.SerializeObject(data), generating);
            prompt.value = (string)payload["messages"][0]["content"];
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(settings.LlmTimeout(generating)) };
            using var body = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("http://127.0.0.1:11434/api/chat", body, cancellation.Token);
            string json = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            if (closed || cancellation.IsCancellationRequested) throw new OperationCanceledException();
            LastModel = (string)JObject.Parse(json)["model"];
            raw.value += json + "\n\n";
            if (LastModel != settings.OllamaModel) throw new InvalidOperationException($"Model mismatch: requested {settings.OllamaModel}, received {LastModel}");
            return (payload["format"]?["properties"]?["evidence"] != null
                ? InterviewController.ParseFollowupCoverageReply(json) : InterviewController.ParseOfficerReply(json, generating))
                ?? throw new FormatException("Invalid JSON reply — no pass/fail applied.");
        }

        private async Task Run(Func<Task> action)
        {
            if (Busy || settings == null) return;
            cancellation = new CancellationTokenSource(); var timer = Stopwatch.StartNew();
            controls.SetEnabled(false); cancel.SetEnabled(true);
            status.text = $"Waiting for {settings.OllamaModel}... First load may take longer.";
            try { await action(); if (!closed) status.text = $"Actual model: {LastModel} | {timer.Elapsed.TotalSeconds:F1}s | {LastResult}"; }
            catch (Exception error) { if (!closed) { LastResult = "ERROR"; status.text = error is OperationCanceledException ? "Cancelled / timed out. No pass/fail applied." : error.GetBaseException().Message; } }
            finally { cancellation?.Dispose(); cancellation = null; if (!closed) { controls.SetEnabled(true); cancel.SetEnabled(false); RefreshButtons(); } }
        }

        private void OnDisable() { closed = true; cancellation?.Cancel(); }
    }
}
