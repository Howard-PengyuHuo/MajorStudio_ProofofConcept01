using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;

namespace CarryOn.Taylor
{
    public class PreparationView : MonoBehaviour
    {
        [SerializeField] private PreparationSession session;
        [SerializeField] private PlayerRole role;
        [SerializeField] private TMP_Text timer, status;
        [SerializeField] private GameObject preparationPanel, interviewPanel, rulesPanel;
        [SerializeField] private TMP_InputField nameInput;
        [UnityEngine.Serialization.FormerlySerializedAs("nationalityInput"), SerializeField] private TMP_InputField birthPlaceInput;
        [UnityEngine.Serialization.FormerlySerializedAs("ageInput"), SerializeField] private TMP_InputField issueDateInput;
        [UnityEngine.Serialization.FormerlySerializedAs("licenseInput"), SerializeField] private TMP_InputField passportCodeInput;
        [SerializeField] private TMP_InputField expiryDateInput;
        [SerializeField] private TMP_InputField birthdayInput;
        [SerializeField] private UnityEngine.UI.Button printButton;
        [SerializeField] private TMP_Text printButtonLabel;
        [Header("Enter to print this document")]
        [SerializeField] private TMP_Text printHint;
        [SerializeField] private AudioSource printAudioSource;
        [SerializeField] private GameObject printedStamp;
        [SerializeField] private Color readyHintColor = new Color(.65f, 1f, .3f);
        private bool printQueued, printSent, wasPrinted;
        private int lastPrintFrame = -1;
        private Coroutine focusRoutine;
        private TMP_InputField[] fields;
        private TMP_InputField[] Fields => fields ??= new[] { nameInput, birthdayInput, birthPlaceInput, passportCodeInput };
        [SerializeField] private GameObject printedCard;
        [SerializeField] private TMP_Text printedName;
        [UnityEngine.Serialization.FormerlySerializedAs("printedNationality"), SerializeField] private TMP_Text printedBirthPlace;
        [UnityEngine.Serialization.FormerlySerializedAs("printedAge"), SerializeField] private TMP_Text printedIssueDate;
        [UnityEngine.Serialization.FormerlySerializedAs("printedLicense"), SerializeField] private TMP_Text printedPassportCode;
        [SerializeField] private TMP_Text printedExpiryDate;
        [SerializeField] private TMP_Text printedBirthday;
        private readonly Dictionary<TMP_InputField, string> pendingEdits = new();

        public void Bind(PreparationSession source)
        {
            if (isActiveAndEnabled) OnDisable();
            pendingEdits.Clear();
            session = source;
            if (isActiveAndEnabled) OnEnable();
        }
        private void OnEnable()
        {
            if (session == null) return;
            session.Changed += Refresh;
            printQueued = printSent = false;
            lastPrintFrame = -1;
            pendingEdits.Clear();
            wasPrinted = session.HasPrintedIdentity;
            if (printButton != null) printButton.gameObject.SetActive(false);
            if (role == PlayerRole.Hacker)
                foreach (var input in Fields)
                    if (input != null) input.onSubmit.AddListener(SubmitDocument);
            if (nameInput != null)
            {
                nameInput.onValueChanged.AddListener(EditName);
                birthPlaceInput?.onValueChanged.AddListener(EditBirthPlace);
                issueDateInput?.onValueChanged.AddListener(EditIssueDate);
                expiryDateInput?.onValueChanged.AddListener(EditExpiryDate);
                birthdayInput?.onValueChanged.AddListener(EditBirthday);
                passportCodeInput?.onValueChanged.AddListener(EditPassportCode);
            }
            Refresh();
        }
        private void OnDisable()
        {
            if (session != null) session.Changed -= Refresh;
            foreach (var input in Fields)
                if (input != null) input.onSubmit.RemoveListener(SubmitDocument);
            if (focusRoutine != null) StopCoroutine(focusRoutine);
            focusRoutine = null;
            ClearHighlights();
            if (nameInput == null) return;
            nameInput.onValueChanged.RemoveListener(EditName);
            birthPlaceInput?.onValueChanged.RemoveListener(EditBirthPlace);
            issueDateInput?.onValueChanged.RemoveListener(EditIssueDate);
            expiryDateInput?.onValueChanged.RemoveListener(EditExpiryDate);
            birthdayInput?.onValueChanged.RemoveListener(EditBirthday);
            passportCodeInput?.onValueChanged.RemoveListener(EditPassportCode);
        }
        private void EditName(string value) => Edit(nameInput, IdentityField.Name, value);
        private void EditBirthPlace(string value) => Edit(birthPlaceInput, IdentityField.BirthPlace, value);
        private void EditIssueDate(string value) => Edit(issueDateInput, IdentityField.IssueDate, value);
        private void EditExpiryDate(string value) => Edit(expiryDateInput, IdentityField.ExpiryDate, value);
        private void EditBirthday(string value) => Edit(birthdayInput, IdentityField.Birthday, value);
        private void EditPassportCode(string value) => Edit(passportCodeInput, IdentityField.PassportCode, value);

        private void Edit(TMP_InputField input, IdentityField field, string value)
        {
            if (printQueued || session.HasPrintedIdentity) return;
            ClearHighlights();
            // Register before sending: local simulation can acknowledge synchronously.
            pendingEdits[input] = value;
            session.RequestIdentityEdit(field, value);
            RefreshPrintHint();
        }

        private void SubmitDocument(string ignored)
        {
            if (role != PlayerRole.Hacker || session == null || !isActiveAndEnabled ||
                session.Phase != EntryPhase.Preparation || session.HasPrintedIdentity ||
                printQueued || lastPrintFrame == Time.frameCount) return;
            lastPrintFrame = Time.frameCount;
            ClearHighlights();
            foreach (var input in Fields)
            {
                if (input != null && !string.IsNullOrWhiteSpace(input.text) &&
                    (!IsDateInput(input) || PassportDate.TryNormalize(input.text, out _))) continue;
                if (input == null) return;
                var outline = input.GetComponent<UnityEngine.UI.Outline>();
                if (outline != null) outline.enabled = true;
                if (focusRoutine != null) StopCoroutine(focusRoutine);
                focusRoutine = StartCoroutine(FocusMissing(input));
                RefreshPrintHint();
                return;
            }
            printQueued = true;
            printSent = false;
            Refresh();
        }

        private IEnumerator FocusMissing(TMP_InputField input)
        {
            yield return null; // Let TMP finish the Enter submission before changing focus.
            if (input != null && input.isActiveAndEnabled && input.interactable)
            {
                input.Select();
                input.ActivateInputField();
            }
            focusRoutine = null;
        }

        private void ClearHighlights()
        {
            foreach (var input in Fields)
            {
                if (input == null) continue;
                var outline = input.GetComponent<UnityEngine.UI.Outline>();
                if (outline != null) outline.enabled = false;
            }
        }

        private void RefreshPrintHint()
        {
            if (printHint == null || role != PlayerRole.Hacker || session == null) return;
            bool complete = true;
            foreach (var input in Fields)
                if (input == null || string.IsNullOrWhiteSpace(input.text)) complete = false;
            string dateError = null;
            foreach (var input in Fields)
                if (input != null && IsDateInput(input) && !string.IsNullOrWhiteSpace(input.text) &&
                    !PassportDate.TryNormalize(input.text, out _))
                {
                    dateError = (input == birthdayInput ? "Birthday" : input == issueDateInput ? "Issued date" : "Expiry date") +
                        ": invalid or ambiguous date. " + PassportDate.Hint;
                    complete = false;
                    break;
                }
            printHint.text = session.HasPrintedIdentity ? "PRINTED / Delivered to your partner" :
                session.Phase != EntryPhase.Preparation ? "Printing is available during the queue." :
                printQueued ? "PASSPORT / PRINTING..." : dateError != null ? dateError : complete ? "PASSPORT / ENTER TO PRINT" :
                "Fill in all fields, then press Enter to print.";
            printHint.color = complete || session.HasPrintedIdentity ? readyHintColor : Color.white;
        }

        private bool IsDateInput(TMP_InputField input) =>
            input == birthdayInput || input == issueDateInput || input == expiryDateInput;

        private void Refresh()
        {
            if (!session.HasPrintedIdentity && session.Phase != EntryPhase.Preparation)
            {
                printQueued = printSent = false;
                lastPrintFrame = -1;
                ClearHighlights();
            }
            if (session.HasPrintedIdentity && !wasPrinted && role == PlayerRole.Hacker)
            {
                if (printAudioSource != null && printAudioSource.clip != null)
                    printAudioSource.PlayOneShot(printAudioSource.clip);
            }
            if (wasPrinted && !session.HasPrintedIdentity)
            {
                printQueued = printSent = false;
                lastPrintFrame = -1;
            }
            wasPrinted = session.HasPrintedIdentity;
            if (printedStamp != null) printedStamp.SetActive(session.HasPrintedIdentity);
            bool interview = session.Phase == EntryPhase.Interview;
            bool editing = session.Phase == EntryPhase.Preparation && !session.HasPrintedIdentity;
            if (printButton != null) printButton.interactable = session.CanPrint;
            if (printButtonLabel != null) printButtonLabel.text = session.HasPrintedIdentity ? "PRINTED" : "PRINT";
            if (printedCard != null) printedCard.SetActive(session.Phase == EntryPhase.Preparation && session.HasPrintedIdentity);
            if (session.HasPrintedIdentity)
            {
                var printed = session.PrintedIdentity;
                if (printedName != null) printedName.text = printed.Name;
                if (printedBirthPlace != null) printedBirthPlace.text = printed.BirthPlace;
                if (printedIssueDate != null) printedIssueDate.text = printed.IssueDate;
                if (printedExpiryDate != null) printedExpiryDate.text = printed.ExpiryDate;
                if (printedBirthday != null) printedBirthday.text = printed.Birthday;
                if (printedPassportCode != null) printedPassportCode.text = printed.PassportCode;
            }
            int seconds = Mathf.CeilToInt(session.RemainingSeconds);
            if (timer != null) timer.text = session.Phase == EntryPhase.Waiting ? "READY TO QUEUE" :
                interview ? "AT THE CHECKPOINT" : $"QUEUE  {seconds / 60:00}:{seconds % 60:00}";
            if (preparationPanel != null) preparationPanel.SetActive(!interview);
            if (interviewPanel != null) interviewPanel.SetActive(interview);
            if (rulesPanel != null) rulesPanel.SetActive(role == PlayerRole.Traveler ? !interview :
                interview || session.HackerCanReadRulesDuringPreparation);
            if (role == PlayerRole.Traveler)
            {
                if (status != null) status.text = interview ? "Officer questions will appear here in the next step." :
                    "Memorize the entry regulations before the queue ends.";
            }
            else
            {
                var card = session.Identity;
                SetInput(nameInput, card.Name, editing);
                SetInput(birthPlaceInput, card.BirthPlace, editing);
                SetInput(issueDateInput, card.IssueDate, editing);
                SetInput(expiryDateInput, card.ExpiryDate, editing);
                SetInput(birthdayInput, card.Birthday, editing);
                SetInput(passportCodeInput, card.PassportCode, editing);
                // Keep local drafts while waiting for the authority to acknowledge the last keystroke.
                if (printQueued && editing)
                    foreach (var input in Fields)
                        if (input != null) input.interactable = false;
                if (status != null) status.text = interview ? "IDENTITY LOCKED  /  Use your card and regulations to help your partner." :
                    !editing ? "Start the queue to edit the identity card." :
                    card.IsComplete ? "All fields filled. You can edit until time expires. Changes save automatically." :
                    "Fill in name, birthday, nationality and passport code. Press Enter to print.";
            }
            RefreshPrintHint();
            if (printQueued && !printSent && pendingEdits.Count == 0 && session.CanPrint)
            {
                printSent = true; // Local simulation can acknowledge synchronously.
                session.RequestPrint();
            }
        }
        private void SetInput(TMP_InputField input, string value, bool editable)
        {
            if (input == null) return;
            value ??= string.Empty;
            input.interactable = editable;
            // Timer ticks and older network echoes must not overwrite a newer local draft,
            // even after Tab/click moves focus to another field.
            if (!editable) pendingEdits.Remove(input);
            else if (pendingEdits.TryGetValue(input, out string pending))
            {
                if (value != pending) return;
                pendingEdits.Remove(input);
            }
            // Never reassign the active editor (TMP may still be processing the keystroke).
            // Print, phase changes and new-round resets still apply authoritative values.
            if (editable && input.isFocused) return;
            if (input.text != value) input.SetTextWithoutNotify(value);
        }
    }
}
