using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CarryOn.Taylor
{
    public sealed class GovernmentDocumentView : MonoBehaviour
    {
        public enum ViewMode { HackerEditor, TravelerCopy, HackerReference }
        [SerializeField] private PreparationSession session;
        [SerializeField] private ViewMode mode;
        [SerializeField] private GameObject document;
        [SerializeField] private TMP_InputField nameInput, nationalityInput, positionInput;
        [SerializeField] private TMP_Text nameText, nationalityText, positionText, printHint;
        [Tooltip("In order: Politics, Cultural Connection, Business Collaboration, Other Category.")]
        [SerializeField] private UnityEngine.UI.Button[] categoryButtons;
        [SerializeField] private UnityEngine.UI.Image[] categoryCircles;
        [SerializeField] private AudioSource printAudioSource;
        [SerializeField] private GameObject printedStamp;
        private readonly Dictionary<TMP_InputField, string> pending = new();
        private UnityAction[] categoryClicks;
        private TaskCategory? pendingCategory;
        private bool queued, sent, wasPrinted;
        private int lastSubmitFrame = -1;
        private TMP_InputField[] inputs;
        private Transform PrintRoot => document != null ? document.transform : null;
        private TMP_InputField[] Inputs => inputs ??= new[] { nameInput, nationalityInput, positionInput };

        private void OnEnable()
        {
            if (session == null) return;
            if (mode == ViewMode.HackerEditor) DocumentPrintTarget.Register(PrintRoot);
            queued = sent = false;
            pending.Clear(); pendingCategory = null;
            wasPrinted = session.HasPrintedGovernment;
            session.Changed += Refresh;
            if (mode == ViewMode.HackerEditor)
            {
                nameInput?.onValueChanged.AddListener(EditName);
                nationalityInput?.onValueChanged.AddListener(EditNationality);
                positionInput?.onValueChanged.AddListener(EditPosition);
                foreach (var input in Inputs) input?.onSubmit.AddListener(Submit);
                categoryClicks = new UnityAction[categoryButtons.Length];
                for (int i = 0; i < categoryButtons.Length; i++)
                {
                    var category = (TaskCategory)(i + 1);
                    categoryClicks[i] = () => SelectCategory(category);
                    categoryButtons[i]?.onClick.AddListener(categoryClicks[i]);
                }
            }
            Refresh();
        }

        private void OnDisable()
        {
            if (mode == ViewMode.HackerEditor) DocumentPrintTarget.Unregister(PrintRoot);
            if (session != null) session.Changed -= Refresh;
            nameInput?.onValueChanged.RemoveListener(EditName);
            nationalityInput?.onValueChanged.RemoveListener(EditNationality);
            positionInput?.onValueChanged.RemoveListener(EditPosition);
            foreach (var input in Inputs) input?.onSubmit.RemoveListener(Submit);
            if (categoryClicks != null)
                for (int i = 0; i < categoryClicks.Length; i++) categoryButtons[i]?.onClick.RemoveListener(categoryClicks[i]);
            StopAllCoroutines();
        }

        private void Update()
        {
            if (mode == ViewMode.HackerEditor && DocumentPrintTarget.EnterPressed && DocumentPrintTarget.IsTarget(PrintRoot))
                Submit("");
        }

        private void EditName(string value) => Edit(nameInput, GovernmentField.Name, value);
        private void EditNationality(string value) => Edit(nationalityInput, GovernmentField.Nationality, value);
        private void EditPosition(string value) => Edit(positionInput, GovernmentField.Position, value);
        private void Edit(TMP_InputField input, GovernmentField field, string value)
        {
            if (queued || session.HasPrintedGovernment) return;
            pending[input] = value;
            session.RequestGovernmentEdit(field, value);
            Refresh();
        }
        private void SelectCategory(TaskCategory category)
        {
            if (queued || session.HasPrintedGovernment || session.Phase != EntryPhase.Preparation) return;
            pendingCategory = category;
            session.RequestGovernmentEdit(GovernmentField.Category, ((int)category).ToString());
            Refresh();
        }
        private void Submit(string ignored)
        {
            if (!DocumentPrintTarget.IsTarget(PrintRoot)) return;
            if (session == null || mode != ViewMode.HackerEditor || session.Phase != EntryPhase.Preparation ||
                queued || session.HasPrintedGovernment || lastSubmitFrame == Time.frameCount) return;
            lastSubmitFrame = Time.frameCount;
            foreach (var input in Inputs)
            {
                if (input == null) return;
                if (!string.IsNullOrWhiteSpace(input.text)) continue;
                StartCoroutine(FocusMissing(input));
                Refresh(); return;
            }
            if ((pendingCategory ?? session.Government.Category) == TaskCategory.None)
            { Refresh(); categoryButtons[0]?.Select(); return; }
            queued = true; sent = false;
            Refresh();
        }
        private IEnumerator FocusMissing(TMP_InputField input)
        {
            var outline = input.GetComponent<UnityEngine.UI.Outline>();
            if (outline != null) outline.enabled = true;
            yield return null;
            input.Select(); input.ActivateInputField();
        }
        private void SetInput(TMP_InputField input, string value, bool editing)
        {
            if (input == null) return;
            input.interactable = editing && !queued;
            value ??= "";
            if (!editing) pending.Remove(input);
            else if (pending.TryGetValue(input, out var draft))
            {
                if (draft != value) return;
                pending.Remove(input);
            }
            if (!(editing && input.isFocused) && input.text != value) input.SetTextWithoutNotify(value);
            var outline = input.GetComponent<UnityEngine.UI.Outline>();
            if (outline != null && !string.IsNullOrWhiteSpace(input.text)) outline.enabled = false;
        }
        private void Refresh()
        {
            bool printed = session.HasPrintedGovernment;
            if (printedStamp != null) printedStamp.SetActive(printed);
            if (wasPrinted && !printed) { queued = sent = false; pending.Clear(); pendingCategory = null; lastSubmitFrame = -1; }
            if (session.Phase != EntryPhase.Preparation) { queued = sent = false; pending.Clear(); pendingCategory = null; }
            if (printed && !wasPrinted && mode == ViewMode.HackerEditor && printAudioSource != null && printAudioSource.clip != null)
                printAudioSource.PlayOneShot(printAudioSource.clip);
            wasPrinted = printed;
            var data = printed ? session.PrintedGovernment : session.Government;
            bool editing = session.Phase == EntryPhase.Preparation && !printed;
            if (!editing || pendingCategory == data.Category) pendingCategory = null;
            if (mode == ViewMode.HackerEditor)
            {
                SetInput(nameInput, data.Name, editing);
                SetInput(nationalityInput, data.Nationality, editing);
                SetInput(positionInput, data.Position, editing);
            }
            else
            {
                if (nameText != null) nameText.text = data.Name ?? "";
                if (nationalityText != null) nationalityText.text = data.Nationality ?? "";
                if (positionText != null) positionText.text = data.Position ?? "";
                if (mode == ViewMode.TravelerCopy && document != null)
                    document.SetActive(printed && session.Phase != EntryPhase.Interview);
            }
            var category = pendingCategory ?? data.Category;
            for (int i = 0; i < categoryCircles.Length; i++)
                if (categoryCircles[i] != null) categoryCircles[i].gameObject.SetActive((int)category == i + 1);
            if (mode == ViewMode.HackerEditor)
            {
                foreach (var button in categoryButtons) if (button != null) button.interactable = editing && !queued;
                bool complete = category != TaskCategory.None;
                foreach (var input in Inputs) if (input == null || string.IsNullOrWhiteSpace(input.text)) complete = false;
                if (printHint != null) printHint.text = printed ? "GOVERNMENT PAPER / PRINTED" : queued ? "PRINTING..." :
                    complete ? "HOVER HERE + ENTER TO PRINT" : "Fill all fields and circle a category. Hover here + Enter to print.";
                if (queued && !sent && pending.Count == 0 && !pendingCategory.HasValue && session.CanPrintGovernment)
                { sent = true; session.RequestGovernmentPrint(); }
            }
        }
    }
}
