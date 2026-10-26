using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CarryOn.Taylor
{
    public enum AnswerSource { FixedAnswer, ForgedName, ForgedNationality }

    [Serializable]
    public class InterviewQuestion
    {
        [TextArea(2, 4)] public string question;
        public AnswerSource answerSource;
        [Tooltip("Used only when Answer Source is Fixed Answer.")]
        public string answer;
    }

    // Demonstration logic only; network authority can replace the local submission later.
    public class InterviewController : MonoBehaviour
    {
        [SerializeField] private PreparationSession session;
        [SerializeField] private TMP_Text questionText;
        [SerializeField] private TMP_InputField answerInput;
        [SerializeField] private InterviewQuestion[] questions =
        {
            new InterviewQuestion { question = "What is your full name?", answerSource = AnswerSource.ForgedName },
            new InterviewQuestion { question = "What is your nationality?", answerSource = AnswerSource.ForgedNationality },
            new InterviewQuestion { question = "What is the purpose of your visit?", answerSource = AnswerSource.FixedAnswer, answer = "believer" }
        };
        [SerializeField] private string successMessage = "ENTRY APPROVED";
        [SerializeField] private string failureMessage = "ENTRY DENIED";
        [Header("Reputation")]
        [SerializeField, Min(1)] private int startingReputation = 50;
        [SerializeField, Min(1)] private int shakePenalty = 20;
        [SerializeField] private TMP_Text reputationText;
        [SerializeField] private UnityEngine.UI.Button shakeButton;
        [SerializeField] private UnityEvent onShakeRequested = new UnityEvent();
        [Header("Shared result panel")]
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private TMP_Text resultText, reasonText;
        [SerializeField] private string successReason = "You may enter Hyacinth Town.";
        [SerializeField] private string wrongAnswerReason = "Your answer does not match the required information.";
        [SerializeField] private string reputationFailureReason = "Your suspicious behavior has caused entry to be denied.";
        [SerializeField] private UnityEvent onEntryApproved = new UnityEvent();
        [SerializeField] private UnityEvent onEntryDenied = new UnityEvent();
        public bool IsFinished { get; private set; }
        public bool EntryApproved { get; private set; }
        public int QuestionIndex { get; private set; }
        public int Reputation { get; private set; }
        public string ResultReason { get; private set; } = string.Empty;
        public event Action Finished;
        public event Action Changed;
        public event Action<int, string> AnswerRequested;
        public event Action ShakeRequested;
        private bool networkMode;
        private bool networkAuthority;
        private bool localInputAllowed = true;
        private bool started;
        private IdentityCard identity;
        private int lastSubmitFrame = -1;

        public void EnterNetworkMode(bool canSubmit, bool isAuthority = false)
        {
            networkMode = true;
            networkAuthority = isAuthority;
            localInputAllowed = canSubmit;
        }

        private void OnEnable()
        {
            if (session != null)
            {
                session.PreparationCompleted += BeginInterview;
                session.Changed += ResetForPreparation;
            }
            if (answerInput != null) answerInput.onSubmit.AddListener(SubmitAnswer);
            if (shakeButton != null) shakeButton.onClick.AddListener(Shake);
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
            Reputation = Mathf.Max(1, startingReputation);
            RefreshReputation();
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.PreparationCompleted -= BeginInterview;
                session.Changed -= ResetForPreparation;
            }
            if (answerInput != null) answerInput.onSubmit.RemoveListener(SubmitAnswer);
            if (shakeButton != null) shakeButton.onClick.RemoveListener(Shake);
        }

        private void ResetForPreparation()
        {
            if (session.Phase == EntryPhase.Interview) return;
            started = false;
            IsFinished = false;
            EntryApproved = false;
            ResultReason = string.Empty;
            QuestionIndex = 0;
            lastSubmitFrame = -1;
            if (answerInput != null) answerInput.SetTextWithoutNotify(string.Empty);
            Reputation = Mathf.Max(1, startingReputation);
            RefreshReputation();
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
            if (shakeButton != null) shakeButton.interactable = false;
        }

        public void BeginInterview(IdentityCard card)
        {
            identity = card;
            QuestionIndex = 0;
            IsFinished = false;
            EntryApproved = false;
            ResultReason = string.Empty;
            started = true;
            lastSubmitFrame = -1;
            Reputation = Mathf.Max(1, startingReputation);
            RefreshReputation();
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
            if (shakeButton != null) shakeButton.interactable = true;
            if (questions == null || questions.Length == 0 || questionText == null || answerInput == null)
            {
                started = false;
                Debug.LogError("InterviewController: assign questions, Question Text and Answer Input in the Inspector.", this);
                return;
            }
            ShowQuestion();
        }

        // TMP onSubmit is Enter; onEndEdit is deliberately not used (clicking away must not submit).
        public void SubmitAnswer(string value)
        {
            if (!localInputAllowed || !started || IsFinished || session.Phase != EntryPhase.Interview ||
                !answerInput.gameObject.activeInHierarchy || lastSubmitFrame == Time.frameCount) return;
            if (string.IsNullOrWhiteSpace(value)) { FocusAnswer(); return; }
            lastSubmitFrame = Time.frameCount;
            if (networkMode)
            {
                AnswerRequested?.Invoke(QuestionIndex, value);
                return;
            }
            EvaluateAnswer(value);
        }

        // Authority entry point: deliberately independent of local panel visibility/input focus.
        public bool EvaluateAnswer(string value)
        {
            if (networkMode && !networkAuthority) return false;
            if (!started || IsFinished || session.Phase != EntryPhase.Interview ||
                string.IsNullOrWhiteSpace(value) || questions == null ||
                QuestionIndex < 0 || QuestionIndex >= questions.Length) return false;
            var current = questions[QuestionIndex];
            string expected = current.answerSource == AnswerSource.ForgedName ? identity.Name :
                current.answerSource == AnswerSource.ForgedNationality ? identity.Nationality : current.answer;
            if (!string.Equals(value.Trim(), (expected ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Finish(false, wrongAnswerReason);
                return true;
            }
            QuestionIndex++;
            if (QuestionIndex >= questions.Length) Finish(true, successReason);
            else ShowQuestion();
            Changed?.Invoke();
            return true;
        }

        private void ShowQuestion()
        {
            questionText.text = questions[QuestionIndex].question;
            answerInput.interactable = localInputAllowed;
            answerInput.SetTextWithoutNotify("");
            FocusAnswer();
        }

        public void FocusAnswer()
        {
            if (localInputAllowed && isActiveAndEnabled && started && !IsFinished) StartCoroutine(FocusNextFrame());
        }

        private IEnumerator FocusNextFrame()
        {
            yield return null; // Wait for TMP to finish processing the previous Enter key.
            if (!IsFinished && answerInput != null && answerInput.gameObject.activeInHierarchy)
                answerInput.ActivateInputField();
        }

        public void Shake()
        {
            if (!localInputAllowed || !started || IsFinished || session.Phase != EntryPhase.Interview ||
                shakeButton == null || !shakeButton.gameObject.activeInHierarchy) return;
            if (networkMode)
            {
                ShakeRequested?.Invoke();
                return;
            }
            ApplyShake();
        }

        public bool ApplyShake()
        {
            if (networkMode && !networkAuthority) return false;
            if (!started || IsFinished || session.Phase != EntryPhase.Interview) return false;
            Reputation = Mathf.Max(0, Reputation - Mathf.Max(1, shakePenalty));
            RefreshReputation();
            if (!networkMode) onShakeRequested.Invoke();
            if (Reputation == 0) Finish(false, reputationFailureReason);
            else FocusAnswer();
            Changed?.Invoke();
            return true;
        }

        // Presentation only. The adapter calls this once per replicated help-request sequence.
        public void NotifyNetworkShake() => onShakeRequested.Invoke();

        public void ApplyNetworkSnapshot(int questionIndex, int reputation, bool finished,
            bool approved, string reason)
        {
            if (!networkMode) return;
            bool questionChanged = QuestionIndex != questionIndex;
            QuestionIndex = questionIndex;
            Reputation = Mathf.Max(0, reputation);
            RefreshReputation();
            if (finished)
            {
                Finish(approved, reason);
                return;
            }
            if (started && !IsFinished && questionChanged) ShowQuestion();
            if (shakeButton != null) shakeButton.interactable = localInputAllowed && started && !IsFinished;
        }

        private void RefreshReputation()
        {
            if (reputationText != null) reputationText.text = Reputation.ToString();
        }

        private void Finish(bool approved, string reason)
        {
            if (IsFinished) return;
            IsFinished = true;
            EntryApproved = approved;
            ResultReason = reason ?? string.Empty;
            questionText.text = approved ? successMessage : failureMessage;
            answerInput.interactable = false;
            if (shakeButton != null) shakeButton.interactable = false;
            if (resultText != null) resultText.text = approved ? successMessage : failureMessage;
            if (reasonText != null) reasonText.text = reason;
            if (gameOverPanel != null)
            {
                gameOverPanel.transform.SetAsLastSibling();
                gameOverPanel.SetActive(true);
            }
            Finished?.Invoke();
            if (approved) onEntryApproved.Invoke();
            else onEntryDenied.Invoke();
            Changed?.Invoke();
        }
    }
}
