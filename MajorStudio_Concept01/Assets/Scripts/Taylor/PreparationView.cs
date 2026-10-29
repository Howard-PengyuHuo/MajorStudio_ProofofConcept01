using System.Collections.Generic;
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
        [SerializeField] private TMP_InputField nameInput, nationalityInput, ageInput, licenseInput;
        [SerializeField] private UnityEngine.UI.Button printButton;
        [SerializeField] private TMP_Text printButtonLabel;
        [SerializeField] private GameObject printedCard;
        [SerializeField] private TMP_Text printedName, printedNationality, printedAge, printedLicense;
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
            if (printButton != null) printButton.onClick.AddListener(session.RequestPrint);
            if (nameInput != null)
            {
                nameInput.onValueChanged.AddListener(EditName);
                nationalityInput?.onValueChanged.AddListener(EditNationality);
                ageInput?.onValueChanged.AddListener(EditAge);
                licenseInput?.onValueChanged.AddListener(EditLicense);
            }
            Refresh();
        }
        private void OnDisable()
        {
            if (session != null) session.Changed -= Refresh;
            if (printButton != null && session != null) printButton.onClick.RemoveListener(session.RequestPrint);
            if (nameInput == null) return;
            nameInput.onValueChanged.RemoveListener(EditName);
            nationalityInput?.onValueChanged.RemoveListener(EditNationality);
            ageInput?.onValueChanged.RemoveListener(EditAge);
            licenseInput?.onValueChanged.RemoveListener(EditLicense);
        }
        private void EditName(string value) => Edit(nameInput, IdentityField.Name, value);
        private void EditNationality(string value) => Edit(nationalityInput, IdentityField.Nationality, value);
        private void EditAge(string value) => Edit(ageInput, IdentityField.Age, value);
        private void EditLicense(string value) => Edit(licenseInput, IdentityField.LicenseNumber, value);

        private void Edit(TMP_InputField input, IdentityField field, string value)
        {
            // Register before sending: local simulation can acknowledge synchronously.
            pendingEdits[input] = value;
            session.RequestIdentityEdit(field, value);
        }

        private void Refresh()
        {
            bool interview = session.Phase == EntryPhase.Interview;
            bool editing = session.Phase == EntryPhase.Preparation && !session.HasPrintedIdentity;
            if (printButton != null) printButton.interactable = session.CanPrint;
            if (printButtonLabel != null) printButtonLabel.text = session.HasPrintedIdentity ? "PRINTED" : "PRINT";
            if (printedCard != null) printedCard.SetActive(session.Phase == EntryPhase.Preparation && session.HasPrintedIdentity);
            if (session.HasPrintedIdentity)
            {
                var printed = session.PrintedIdentity;
                if (printedName != null) printedName.text = printed.Name;
                if (printedNationality != null) printedNationality.text = printed.Nationality;
                if (printedAge != null) printedAge.text = printed.Age;
                if (printedLicense != null) printedLicense.text = printed.LicenseNumber;
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
                SetInput(nationalityInput, card.Nationality, editing);
                SetInput(ageInput, card.Age, editing);
                SetInput(licenseInput, card.LicenseNumber, editing);
                if (status != null) status.text = interview ? "IDENTITY LOCKED  /  Use your card and regulations to help your partner." :
                    !editing ? "Start the queue to edit the identity card." :
                    card.IsComplete ? "All fields filled. You can edit until time expires. Changes save automatically." :
                    "Type into all four fields. Changes save automatically.";
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
