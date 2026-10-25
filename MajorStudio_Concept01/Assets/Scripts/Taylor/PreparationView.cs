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

        public void Bind(PreparationSession source)
        {
            if (isActiveAndEnabled) OnDisable();
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
        private void EditName(string value) => session.RequestIdentityEdit(IdentityField.Name, value);
        private void EditNationality(string value) => session.RequestIdentityEdit(IdentityField.Nationality, value);
        private void EditAge(string value) => session.RequestIdentityEdit(IdentityField.Age, value);
        private void EditLicense(string value) => session.RequestIdentityEdit(IdentityField.LicenseNumber, value);

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
        private static void SetInput(TMP_InputField input, string value, bool editable)
        {
            if (input == null) return;
            input.SetTextWithoutNotify(value ?? string.Empty);
            input.interactable = editable;
        }
    }
}
