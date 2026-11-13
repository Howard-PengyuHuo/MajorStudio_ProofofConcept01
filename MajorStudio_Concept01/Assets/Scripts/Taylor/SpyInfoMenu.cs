using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CarryOn.Taylor
{
    // Local desktop presentation only. Documents and menu rows are authored in the prefab.
    public sealed class SpyInfoMenu : MonoBehaviour
    {
        public enum DocumentType { SpyInfo, EntryInfo }

        [Serializable]
        public sealed class DocumentEntry
        {
            public string title;
            public DocumentType type;
            public UnityEngine.UI.Button menuButton;
            public TMP_Text label;
            public GameObject document;
            [NonSerialized] public bool revealed;
            [NonSerialized] public UnityAction click;
            [NonSerialized] public Vector3 initialPosition;
            [NonSerialized] public Quaternion initialRotation;
            [NonSerialized] public Vector3 initialScale;
        }

        [SerializeField] private PreparationSession session;
        [SerializeField] private bool showAllDocumentsOnDesk;
        [SerializeField] private UnityEngine.UI.Button spyInfoButton;
        [SerializeField] private UnityEngine.UI.Button entryInfoButton;
        [SerializeField] private GameObject menuRoot;
        [SerializeField] private RectTransform menuPanel;
        [SerializeField] private UnityEngine.UI.Button outsideButton;
        [SerializeField] private DocumentEntry[] documents = Array.Empty<DocumentEntry>();
        [SerializeField] private string revealedSuffix = "  [ON DESK]";
        private EntryPhase lastPhase;
        private DocumentType openType;

        private void Awake()
        {
            foreach (var entry in documents)
                if (entry?.document != null)
                {
                    entry.initialPosition = entry.document.transform.localPosition;
                    entry.initialRotation = entry.document.transform.localRotation;
                    entry.initialScale = entry.document.transform.localScale;
                }
            ResetDocuments();
        }

        private void OnEnable()
        {
            spyInfoButton?.onClick.AddListener(ToggleMenu);
            entryInfoButton?.onClick.AddListener(ToggleEntryMenu);
            outsideButton?.onClick.AddListener(CloseMenu);
            foreach (var entry in documents)
            {
                if (entry == null || entry.menuButton == null) continue;
                entry.click = () => Reveal(entry);
                entry.menuButton.onClick.AddListener(entry.click);
                RefreshEntry(entry);
            }
            if (session != null)
            {
                session.Changed += OnSessionChanged;
                lastPhase = session.Phase;
                if (lastPhase != EntryPhase.Interview) ResetDocuments();
            }
            CloseMenu();
        }

        private void OnDisable()
        {
            spyInfoButton?.onClick.RemoveListener(ToggleMenu);
            entryInfoButton?.onClick.RemoveListener(ToggleEntryMenu);
            outsideButton?.onClick.RemoveListener(CloseMenu);
            foreach (var entry in documents)
                if (entry?.menuButton != null && entry.click != null)
                    entry.menuButton.onClick.RemoveListener(entry.click);
            if (session != null) session.Changed -= OnSessionChanged;
            CloseMenu();
        }

        private void OnSessionChanged()
        {
            if (session.Phase == lastPhase) return;
            lastPhase = session.Phase;
            if (lastPhase != EntryPhase.Interview) ResetDocuments();
        }

        public void ToggleMenu() => ToggleCategory(DocumentType.SpyInfo, spyInfoButton);
        public void ToggleEntryMenu() => ToggleCategory(DocumentType.EntryInfo, entryInfoButton);

        private void ToggleCategory(DocumentType type, UnityEngine.UI.Button button)
        {
            if (menuRoot == null || button == null) return;
            bool open = !menuRoot.activeSelf || openType != type;
            openType = type;
            foreach (var entry in documents)
                if (entry?.menuButton != null) entry.menuButton.gameObject.SetActive(entry.type == type);
            menuRoot.SetActive(open);
            if (open)
            {
                menuRoot.transform.SetAsLastSibling();
                if (spyInfoButton != null) spyInfoButton.transform.SetAsLastSibling();
                if (entryInfoButton != null) entryInfoButton.transform.SetAsLastSibling();
                // Place the shared, prefab-authored list beneath the selected category button.
                if (menuPanel != null)
                {
                    var corners = new Vector3[4];
                    ((RectTransform)button.transform).GetWorldCorners(corners);
                    menuPanel.pivot = new Vector2(0, 1);
                    menuPanel.position = corners[0];
                    menuPanel.localPosition += new Vector3(0, -6, 0);
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(menuPanel);
                }
            }
        }

        public void CloseMenu()
        {
            if (menuRoot != null) menuRoot.SetActive(false);
        }

        private void Reveal(DocumentEntry entry)
        {
            if (entry.document == null || entry.revealed) return;
            entry.revealed = true;
            entry.document.SetActive(true);
            entry.document.transform.SetAsLastSibling();
            RefreshEntry(entry);
            CloseMenu();
        }

        public void ResetDocuments()
        {
            foreach (var entry in documents)
            {
                if (entry == null) continue;
                entry.revealed = showAllDocumentsOnDesk;
                if (entry.document != null)
                {
                    entry.document.SetActive(showAllDocumentsOnDesk);
                    entry.document.transform.localPosition = entry.initialPosition;
                    entry.document.transform.localRotation = entry.initialRotation;
                    entry.document.transform.localScale = entry.initialScale;
                }
                RefreshEntry(entry);
            }
            CloseMenu();
        }

        private void RefreshEntry(DocumentEntry entry)
        {
            if (entry.label != null) entry.label.text = entry.title + (entry.revealed ? revealedSuffix : "");
            if (entry.menuButton != null) entry.menuButton.interactable = entry.document != null && !entry.revealed;
        }
    }
}
