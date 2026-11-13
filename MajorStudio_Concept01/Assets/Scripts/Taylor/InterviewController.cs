using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CarryOn.Taylor
{
    public enum AnswerSource { FixedAnswer, ForgedName, ForgedBirthPlace, ForgedIssueDate, ForgedExpiryDate, ForgedPassportCode, ForgedBirthday,
        GovernmentName, GovernmentNationality, GovernmentPosition, GovernmentTaskCategory, AnyNonEmpty }

    public enum InterviewTopic { Name, Nationality, VisitPurpose, StayDuration, Belongings, GovernmentRole, RoleDuties, Accommodation,
        TravelCompanions, ArrivalRoute, VisitActivity, StayPlans, ItemUse, AccommodationPlan, PromisedDignity, HeavenlyFatherName, DivineRetribution }

    [Serializable]
    public class InterviewQuestion
    {
        [TextArea(2, 4)] public string question;
        [Tooltip("AI can rephrase this topic, but cannot choose a different subject.")]
        public InterviewTopic topic;
        public AnswerSource answerSource;
        [Tooltip("Used only when Answer Source is Fixed Answer.")]
        public string answer;
        [Tooltip("For Fixed Answer questions whose answer is a date. Forged date fields use date matching automatically.")]
        public bool dateAnswer;
        [Min(0.1f), Tooltip("Time allowed for this question, in seconds.")]
        public float timeLimitSeconds = 30f;
    }

    // Demonstration logic only; network authority can replace the local submission later.
    public partial class InterviewController : MonoBehaviour
    {
        [SerializeField] private PreparationSession session;
        [SerializeField] private TMP_Text questionText;
        [SerializeField] private TMP_InputField answerInput;
        [Header("Opening introduction")]
        [TextArea(2, 5), SerializeField] private string introduction = "Welcome! Allow us to do a quick review before letting you through to Hyacinth Town. Hopefully you have read the entry rules carefully on your way here.";
        [SerializeField, Min(0)] private float introductionSeconds = 3f;
        [Header("Document consistency: passport and permit name / nationality")]
        [TextArea(2, 5), SerializeField] private string discrepancyQuestion = "There’s a discrepancy between your passport and your government permit. These documents do not appear to belong to the same person. How do you explain that?";
        [SerializeField] private string discrepancyReason = "The information on your passport and government permit does not match.";
        public bool DocumentMismatch { get; private set; }
        [SerializeField] private InterviewQuestion[] questions =
        {
            new InterviewQuestion { question = "What is your name, sir?", topic = InterviewTopic.Name, answerSource = AnswerSource.ForgedName },
            new InterviewQuestion { question = "Where are you coming from?", topic = InterviewTopic.Nationality, answerSource = AnswerSource.GovernmentNationality },
            new InterviewQuestion { question = "What is the intention of your trip? Any plans for how you’re going to spend your time here?", topic = InterviewTopic.VisitPurpose, answerSource = AnswerSource.GovernmentTaskCategory },
            new InterviewQuestion { question = "How long are you planning to stay here?", topic = InterviewTopic.StayDuration, answerSource = AnswerSource.AnyNonEmpty },
            new InterviewQuestion { question = "What belongings are you bringing with you?", topic = InterviewTopic.Belongings, answerSource = AnswerSource.AnyNonEmpty }
        };
        [SerializeField] private string successMessage = "ENTRY APPROVED";
        [SerializeField] private string failureMessage = "ENTRY DENIED";
        [Header("Wrong answers")]
        [SerializeField, Min(1)] private int maximumWrongAnswers = 3;
        [SerializeField] private TMP_Text reputationText;
        [SerializeField] private UnityEngine.UI.Button shakeButton;
        [SerializeField] private UnityEvent onShakeRequested = new UnityEvent();
        [Header("Shared result panel")]
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private UnityEngine.UI.Image resultImage;
        [SerializeField] private Sprite successSprite, failureSprite;
        [SerializeField] private UnityEngine.UI.Button restartButton;
        [SerializeField] private TMP_Text resultText, reasonText;
        [SerializeField] private string successReason = "Have a great time during your stay. Live with DIGNITY.";
        [SerializeField] private string wrongAnswerReason = "Your answer does not match the required information.";
        [SerializeField] private string reputationFailureReason = "Your suspicious behavior has caused entry to be denied.";
        [SerializeField] private UnityEvent onEntryApproved = new UnityEvent();
        [SerializeField] private UnityEvent onEntryDenied = new UnityEvent();
        public bool IsFinished { get; private set; }
        public bool EntryApproved { get; private set; }
        public int QuestionIndex { get; private set; }
        public int Reputation { get; private set; }
        public int AcceptedAnswerCount { get; private set; }
        public string ResultReason { get; private set; } = string.Empty;
        public event Action Finished;
        public event Action Changed;
        public event Action<int, string> AnswerRequested;
        public event Action ShakeRequested;
        public event Action ShakePresented;
        public event Action RestartRequested;

        public void RestartGame()
        {
            if (!IsFinished) return;
            if (networkMode) RestartRequested?.Invoke();
            else if (session != null) session.BeginPreparation(session.PreparationSeconds);
        }
        public string CurrentQuestion => started && !IsFinished && questions != null &&
            QuestionIndex >= 0 && QuestionIndex < ActiveQuestions.Length ?
            (IsExplainingDenial ? ResultReason : IsShowingIntroduction ? introduction : DocumentMismatch ? discrepancyQuestion : ReviewingPurpose ? "Reviewing your answer..." :
             !string.IsNullOrEmpty(PurposeLine) ? PurposeLine : ActiveQuestion.question) : string.Empty;
        private bool networkMode;
        private bool networkAuthority;
        private bool localInputAllowed = true;
        private bool started;
        private IdentityCard identity;
        private int lastSubmitFrame = -1;
        private Func<double> networkClock;
        public double QuestionEndsAt { get; private set; }
        public double IntroductionEndsAt { get; private set; }
        public bool IsShowingIntroduction => started && !IsFinished && QuestionIndex == 0 && Clock < IntroductionEndsAt;
        private bool showingIntroduction;
        private double Clock => networkMode && networkClock != null ? networkClock() : Time.unscaledTimeAsDouble;
        public float QuestionSecondsRemaining => QuestionTimerRunning
            ? (float)Math.Max(0, QuestionEndsAt - Clock) : 0;
        public bool QuestionTimerRunning => started && !IsFinished && !IsShowingIntroduction && QuestionEndsAt > 0;
        public void SetNetworkClock(Func<double> clock) => networkClock = clock;

        private void Update()
        {
            if (IsExplainingDenial && (!networkMode || networkAuthority) && Clock >= DenialEndsAt)
            {
                CompleteFinish(EntryApproved, ResultReason);
                PurposeResolved?.Invoke();
            }
            if (showingIntroduction != IsShowingIntroduction)
            {
                RefreshQuestionPresentation();
                if (!IsShowingIntroduction) FocusAnswer();
            }
            if (!networkMode) ExpireQuestion();
        }

        // The network adapter calls this before accepting answers as well as every frame.
        public bool ExpireQuestion()
        {
            if (networkMode && !networkAuthority) return false;
            if (IsExplainingDenial || !QuestionTimerRunning || Clock < QuestionEndsAt) return false;
            Finish(false, "You did not answer the question in time.");
            return true;
        }

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
                if (session.Phase == EntryPhase.Preparation) PreparePurpose();
            }
            if (answerInput != null) answerInput.onSubmit.AddListener(SubmitAnswer);
            if (shakeButton != null) shakeButton.onClick.AddListener(Shake);
            if (restartButton != null) restartButton.onClick.AddListener(RestartGame);
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
            Reputation = Mathf.Max(1, maximumWrongAnswers);
            RefreshReputation();
        }

        private void OnDisable()
        {
            if (session != null)
            {
                ResetPurpose();
                session.PreparationCompleted -= BeginInterview;
                session.Changed -= ResetForPreparation;
            }
            if (answerInput != null) answerInput.onSubmit.RemoveListener(SubmitAnswer);
            if (shakeButton != null) shakeButton.onClick.RemoveListener(Shake);
            if (restartButton != null) restartButton.onClick.RemoveListener(RestartGame);
        }

        private void ResetForPreparation()
        {
            if (session.Phase == EntryPhase.Interview) return;
            if (session.Phase == EntryPhase.Waiting || started) ResetPurpose();
            if (session.Phase == EntryPhase.Preparation) PreparePurpose();
            started = false;
            QuestionEndsAt = 0;
            IntroductionEndsAt = 0;
            showingIntroduction = false;
            DocumentMismatch = false;
            IsFinished = false;
            EntryApproved = false;
            ResultReason = string.Empty;
            QuestionIndex = 0;
            lastSubmitFrame = -1;
            if (answerInput != null) answerInput.SetTextWithoutNotify(string.Empty);
            Reputation = Mathf.Max(1, maximumWrongAnswers);
            RefreshReputation();
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
            if (shakeButton != null) shakeButton.interactable = false;
        }

        public void BeginInterview(IdentityCard card)
        {
            EnsureInterviewPlan();
            identity = card;
            var permit = session.HasPrintedGovernment ? session.PrintedGovernment : session.Government;
            DocumentMismatch = Conflicts(card.Nationality, permit.Nationality) || Conflicts(card.Name, permit.Name);
            QuestionIndex = 0;
            IsFinished = false;
            EntryApproved = false;
            ResultReason = string.Empty;
            started = true;
            IntroductionEndsAt = !networkMode || networkAuthority
                ? Clock + (string.IsNullOrWhiteSpace(introduction) ? 0 : introductionSeconds) : 0;
            lastSubmitFrame = -1;
            Reputation = Mathf.Max(1, maximumWrongAnswers);
            RefreshReputation();
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
            if (shakeButton != null) shakeButton.interactable = true;
            if (questions == null || ActiveQuestions.Length == 0 || questionText == null || answerInput == null)
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
            if (IsExplainingDenial || ReviewingPurpose || !localInputAllowed || !started || IsFinished || IsShowingIntroduction || session.Phase != EntryPhase.Interview ||
                !answerInput.gameObject.activeInHierarchy || lastSubmitFrame == Time.frameCount) return;
            if (string.IsNullOrWhiteSpace(value)) { FocusAnswer(); return; }
            if (!useOllama && !DocumentMismatch && UsesDate(ActiveQuestion) && !PassportDate.TryExtract(value, out _))
            {
                ShowDateHint();
                FocusAnswer();
                return;
            }
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
            if (IsExplainingDenial || ReviewingPurpose) return false;
            if (ExpireQuestion()) return true;
            if (!started || IsFinished || IsShowingIntroduction || session.Phase != EntryPhase.Interview ||
                string.IsNullOrWhiteSpace(value) || questions == null ||
                QuestionIndex < 0 || QuestionIndex >= ActiveQuestions.Length) return false;
            var current = ActiveQuestion;
            if (DocumentMismatch)
            {
                Finish(false, discrepancyReason);
                return true;
            }
            var government = session.HasPrintedGovernment ? session.PrintedGovernment : session.Government;
            if (useOllama)
            {
                answerJob = StartCoroutine(JudgeAnswer(value));
                return true;
            }
            string expected = current.answerSource == AnswerSource.ForgedName ? identity.Name :
                current.answerSource == AnswerSource.ForgedBirthPlace ? identity.BirthPlace :
                current.answerSource == AnswerSource.ForgedIssueDate ? identity.IssueDate :
                current.answerSource == AnswerSource.ForgedExpiryDate ? identity.ExpiryDate :
                current.answerSource == AnswerSource.ForgedBirthday ? identity.Birthday :
                current.answerSource == AnswerSource.ForgedPassportCode ? identity.PassportCode :
                current.answerSource == AnswerSource.GovernmentName ? government.Name :
                current.answerSource == AnswerSource.GovernmentNationality ? government.Nationality :
                current.answerSource == AnswerSource.GovernmentPosition ? government.Position :
                current.answerSource == AnswerSource.GovernmentTaskCategory ? GovernmentDocument.CategoryLabel(government.Category) : current.answer;
            bool matches;
            if (current.answerSource == AnswerSource.ForgedBirthday)
                matches = PassportDate.TryNormalizeBirthYear(expected, out var birthYear) &&
                    System.Text.RegularExpressions.Regex.IsMatch(value, @"(?<!\d)" + birthYear + @"(?!\d)");
            else if (UsesDate(current))
            {
                if (!PassportDate.TryExtract(value, out var submittedDate))
                {
                    ShowDateHint();
                    return false;
                }
                matches = PassportDate.TryNormalize(expected, out var expectedDate) && submittedDate == expectedDate;
            }
            else matches = current.answerSource == AnswerSource.AnyNonEmpty ||
                (!string.IsNullOrWhiteSpace(expected) && NormalizeAnswer(value).IndexOf(
                    NormalizeAnswer(expected), StringComparison.OrdinalIgnoreCase) >= 0);
            if (!matches)
            {
                if (!RegisterWrongAnswer()) ShowQuestion();
                return true;
            }
            AcceptedAnswerCount++;
            AdvanceQuestion();
            return true;
        }

        private void AdvanceQuestion()
        {
            extensionQuestion = null;
            followupParentAnswer = "";
            PurposeLine = "";
            QuestionIndex++;
            if (QuestionIndex >= ActiveQuestions.Length) Finish(true, successReason);
            else ShowQuestion();
            Changed?.Invoke();
        }

        private static string NormalizeAnswer(string value) =>
            System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");

        // Missing information is handled by the normal question rules, not a new missing-document penalty.
        private static bool Conflicts(string first, string second) =>
            !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
            !string.Equals(NormalizeAnswer(first), NormalizeAnswer(second), StringComparison.OrdinalIgnoreCase);

        private static bool UsesDate(InterviewQuestion question) =>
            question.answerSource == AnswerSource.ForgedIssueDate ||
            question.answerSource == AnswerSource.ForgedExpiryDate ||
            (question.answerSource == AnswerSource.FixedAnswer && question.dateAnswer);

        private void ShowDateHint()
        {
            if (questionText != null)
                questionText.text = ActiveQuestion.question + "\n" + PassportDate.Hint;
        }

        private void ShowQuestion()
        {
            if (ReviewingPurpose) { RefreshQuestionPresentation(); return; }
            if ((!networkMode || networkAuthority) && useOllama && !DocumentMismatch && string.IsNullOrEmpty(PurposeLine) &&
                (preparedLines == null || preparedLines[QuestionIndex] == null))
            {
                if (questionJob != null) StopCoroutine(questionJob);
                questionJob = StartCoroutine(GenerateNextQuestion());
                return;
            }
            if ((!networkMode || networkAuthority) && useOllama && !DocumentMismatch && string.IsNullOrEmpty(PurposeLine))
                PurposeLine = preparedLines != null && preparedLines[QuestionIndex] != null ? preparedLines[QuestionIndex] : ActiveQuestion.question;
            if (!networkMode || networkAuthority)
                QuestionEndsAt = Math.Max(Clock, QuestionIndex == 0 ? IntroductionEndsAt : 0) + Mathf.Max(0.1f, ActiveQuestion.timeLimitSeconds);
            RefreshQuestionPresentation();
            answerInput.SetTextWithoutNotify("");
            FocusAnswer();
        }

        private void RefreshQuestionPresentation()
        {
            showingIntroduction = IsShowingIntroduction;
            if (!started || IsFinished) return;
            if (questionText != null) questionText.text = CurrentQuestion;
            if (answerInput != null) answerInput.interactable = localInputAllowed && !showingIntroduction && !ReviewingPurpose && !IsExplainingDenial;
            if (shakeButton != null) shakeButton.interactable = localInputAllowed && !showingIntroduction && !ReviewingPurpose && !IsExplainingDenial;
        }

        public void FocusAnswer()
        {
            if (localInputAllowed && isActiveAndEnabled && started && !IsFinished && !IsShowingIntroduction) StartCoroutine(FocusNextFrame());
        }

        private IEnumerator FocusNextFrame()
        {
            yield return null; // Wait for TMP to finish processing the previous Enter key.
            if (!IsFinished && !IsShowingIntroduction && answerInput != null && answerInput.gameObject.activeInHierarchy)
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
            if (IsExplainingDenial || ReviewingPurpose || !started || IsFinished || IsShowingIntroduction || session.Phase != EntryPhase.Interview) return false;
            if (!networkMode) NotifyNetworkShake();
            FocusAnswer();
            Changed?.Invoke();
            return true;
        }

        // Reputation is the remaining mistake allowance, replicated by the existing bridge.
        // Returns true when the closing line has started and no further question may be shown.
        private bool RegisterWrongAnswer()
        {
            if (IsFinished || IsExplainingDenial || (networkMode && !networkAuthority)) return true;
            Reputation = Mathf.Max(0, Reputation - 1);
            RefreshReputation();
            ReputationDropped?.Invoke();
            if (Reputation == 0)
                Finish(false, "YOUR ANSWERS HAVE FAILED OUR INSPECTION. ENTRY IS DENIED. STEP AWAY FROM THE COUNTER.");
            Changed?.Invoke();
            PurposeResolved?.Invoke();
            return IsExplainingDenial;
        }
        // Presentation only. The adapter calls this once per replicated help-request sequence.
        public void NotifyNetworkShake()
        {
            onShakeRequested.Invoke();
            ShakePresented?.Invoke();
        }

        public void ApplyNetworkSnapshot(int questionIndex, int reputation, bool finished,
            bool approved, string reason, double questionEndsAt = 0, double introductionEndsAt = 0, bool documentMismatch = false,
            string purposeLine = "", bool reviewingPurpose = false, double denialEndsAt = 0, string lastSubmittedAnswer = "", int acceptedAnswerCount = 0)
        {
            if (!networkMode) return;
            if (!networkAuthority) AcceptedAnswerCount = acceptedAnswerCount;
            DenialEndsAt = denialEndsAt;
            ResultReason = reason ?? "";
            EntryApproved = approved;
            bool questionChanged = QuestionIndex != questionIndex || PurposeLine != (purposeLine ?? "") || (ReviewingPurpose && !reviewingPurpose);
            PurposeLine = purposeLine ?? "";
            ReviewingPurpose = reviewingPurpose;
            LastSubmittedAnswer = lastSubmittedAnswer ?? "";
            if (!networkAuthority && reputation < Reputation) ReputationDropped?.Invoke();
            QuestionIndex = questionIndex;
            IntroductionEndsAt = introductionEndsAt;
            DocumentMismatch = documentMismatch;
            Reputation = Mathf.Max(0, reputation);
            RefreshReputation();
            if (finished)
            {
                CompleteFinish(approved, reason);
                return;
            }
            if (started && !IsFinished && !IsExplainingDenial && questionChanged) ShowQuestion();
            QuestionEndsAt = questionEndsAt;
            RefreshQuestionPresentation();
            if (shakeButton != null) shakeButton.interactable = localInputAllowed && started && !IsFinished && !IsShowingIntroduction && !ReviewingPurpose && !IsExplainingDenial;
        }

        private void RefreshReputation()
        {
            if (reputationText != null) reputationText.text = Reputation.ToString();
        }

        private void Finish(bool approved, string reason)
        {
            if (IsFinished || IsExplainingDenial) return;
            EntryApproved = approved;
            ResultReason = reason ?? "Entry denied.";
            QuestionEndsAt = 0;
            ReviewingPurpose = false;
            DenialEndsAt = Clock + Mathf.Clamp(ResultReason.Split(' ').Length / 2.5f, 5f, 10f);
            RefreshQuestionPresentation();
            Changed?.Invoke();
            PurposeResolved?.Invoke();
        }

        private void CompleteFinish(bool approved, string reason)
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
            if (resultImage != null) resultImage.sprite = approved ? successSprite : failureSprite;
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
