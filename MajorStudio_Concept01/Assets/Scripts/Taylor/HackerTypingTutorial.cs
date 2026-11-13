using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CarryOn.Taylor
{
    public sealed class HackerTypingTutorial : MonoBehaviour
    {
        [SerializeField] private PreparationSession session;
        [SerializeField] private GameObject overlay;
        [SerializeField] private TMP_InputField firstNameInput;
        [SerializeField] private TMP_Text hint;
        [SerializeField] private TMP_Text[] letterNames;
        [SerializeField, Min(0)] private float confirmationSeconds = 1;
        private bool opened, completed, submitted;
        private string submittedName;
        private float submittedAt;

        private void OnEnable()
        {
            opened = completed = submitted = false;
            if (session != null) session.Changed += Refresh;
            if (firstNameInput != null) firstNameInput.onSubmit.AddListener(Submit);
            Refresh();
        }

        private void OnDisable()
        {
            if (session != null) session.Changed -= Refresh;
            if (firstNameInput != null) firstNameInput.onSubmit.RemoveListener(Submit);
            Close();
        }

        private void Refresh()
        {
            if (session == null || overlay == null || firstNameInput == null) return;
            if (session.Phase != EntryPhase.Preparation)
            {
                Close();
                if (session.Phase == EntryPhase.Waiting) opened = completed = submitted = false;
                return;
            }
            if (opened || completed) return;
            opened = true;
            submitted = false;
            foreach (var text in letterNames) if (text != null) text.text = "";
            firstNameInput.SetTextWithoutNotify("");
            firstNameInput.interactable = true;
            if (hint != null) hint.text = "PRESS ENTER TO START";
            overlay.SetActive(true);
            overlay.transform.SetAsLastSibling();
            DocumentPrintTarget.InputBlocked = true;
            firstNameInput.Select();
            firstNameInput.ActivateInputField();
        }

        private void Submit(string value)
        {
            if (!opened || submitted || session.Phase != EntryPhase.Preparation) return;
            value = (value ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(value))
            {
                firstNameInput.ActivateInputField();
                return;
            }
            submitted = true;
            submittedName = value;
            submittedAt = Time.unscaledTime;
            firstNameInput.SetTextWithoutNotify(value);
            firstNameInput.interactable = false;
            // Use the same authority requests as normal editing; do not mutate a client-only card.
            session.RequestIdentityEdit(IdentityField.FirstName, value);
            session.RequestGovernmentEdit(GovernmentField.Name, value);
            foreach (var text in letterNames) if (text != null) text.text = value;
            if (hint != null) hint.text = "NAME ENTERED";
        }

        private void Update()
        {
            if (!opened || completed || overlay == null || !overlay.activeSelf) return;
            if (submitted)
            {
                // Wait for both server acknowledgements as well as the one-second reading pause.
                if (Time.unscaledTime - submittedAt >= confirmationSeconds &&
                    session.Identity.FirstName == submittedName && session.Government.Name == submittedName)
                {
                    completed = true;
                    Close();
                }
                return;
            }
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null || !selected.transform.IsChildOf(overlay.transform))
            {
                firstNameInput.Select();
                firstNameInput.ActivateInputField();
            }
        }

        private void Close()
        {
            if (overlay != null) overlay.SetActive(false);
            DocumentPrintTarget.InputBlocked = false;
        }
    }
}
