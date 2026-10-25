using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarryOn.Taylor
{
    /// <summary>Local test controls. Hide these when a network lobby assigns roles.</summary>
    public class PreparationPreview : MonoBehaviour
    {
        [SerializeField] private PreparationSession session;
        [SerializeField] private GameObject traveler, hacker, debugControls;
        [SerializeField] private UnityEngine.UI.Button startButton;
        [SerializeField] private TMP_Text roleLabel;
        [SerializeField] private bool autoStart = true;
        [SerializeField] private PlayerRole startingRole = PlayerRole.Hacker;
        [SerializeField] private GameObject travelerInterview;
        [SerializeField] private GameObject hackerInterview;
        [SerializeField] private InterviewController interview;
        private PlayerRole currentRole;
        private void Start()
        {
            if (session == null) return;
            session.Changed += Refresh;
            if (interview != null) interview.Finished += Refresh;
            if (debugControls != null) debugControls.SetActive(session.LocalSimulation);
            if (session.LocalSimulation)
            {
                SetLocalRole(startingRole);
                if (autoStart) StartQueue();
            }
            Refresh();
        }
        private void Update()
        {
            if (session == null || !session.LocalSimulation || Keyboard.current == null) return;
            if (Keyboard.current.f1Key.wasPressedThisFrame) ShowTraveler();
            if (Keyboard.current.f2Key.wasPressedThisFrame) ShowHacker();
        }
        private void OnDestroy()
        {
            if (session != null) session.Changed -= Refresh;
            if (interview != null) interview.Finished -= Refresh;
        }
        private void Refresh()
        {
            if (startButton != null) startButton.interactable = session.Phase == EntryPhase.Waiting;
            UpdatePanels();
        }
        public void StartQueue() { if (session != null) session.RequestStart(); }
        public void ShowTraveler() => SetLocalRole(PlayerRole.Traveler);
        public void ShowHacker() => SetLocalRole(PlayerRole.Hacker);
        public void SetLocalRole(PlayerRole role)
        {
            currentRole = role;
            UpdatePanels();
            if (roleLabel != null) roleLabel.text = role == PlayerRole.Traveler ? "TRAVELER / ENTRY QUEUE" : "HACKER / IDENTITY TERMINAL";
            if (role == PlayerRole.Traveler && interview != null) interview.FocusAnswer();
        }
        private void UpdatePanels()
        {
            bool atCheckpoint = session != null && session.Phase == EntryPhase.Interview;
            bool isTraveler = currentRole == PlayerRole.Traveler;
            bool running = interview == null || !interview.IsFinished;
            if (traveler != null) traveler.SetActive(running && isTraveler && !atCheckpoint);
            if (hacker != null) hacker.SetActive(running && !isTraveler && (!atCheckpoint || hackerInterview == null));
            if (travelerInterview != null) travelerInterview.SetActive(running && isTraveler && atCheckpoint);
            if (hackerInterview != null) hackerInterview.SetActive(running && !isTraveler && atCheckpoint);
        }
    }
}
