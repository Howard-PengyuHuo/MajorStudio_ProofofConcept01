using System;
using UnityEngine;

namespace CarryOn.Taylor
{
    public enum EntryPhase { Waiting, Preparation, Interview }
    public enum PlayerRole { Traveler, Hacker }
    public enum IdentityField { Name, Nationality, Age, LicenseNumber }

    [Serializable]
    public struct IdentityCard
    {
        public string Name, Nationality, Age, LicenseNumber;
        public bool IsComplete => !string.IsNullOrWhiteSpace(Name) &&
            !string.IsNullOrWhiteSpace(Nationality) && !string.IsNullOrWhiteSpace(Age) &&
            !string.IsNullOrWhiteSpace(LicenseNumber);
    }

    /// <summary>Local prototype authority. A network adapter can disable local timing and apply snapshots.</summary>
    public class PreparationSession : MonoBehaviour
    {
        [SerializeField, Min(1)] private float preparationSeconds = 60;
        [SerializeField] private bool localSimulation = true;
        [SerializeField] private bool hackerCanReadRulesDuringPreparation;
        public EntryPhase Phase { get; private set; }
        public float RemainingSeconds { get; private set; }
        public IdentityCard Identity { get; private set; }
        public IdentityCard PrintedIdentity { get; private set; }
        public bool HasPrintedIdentity { get; private set; }
        public bool CanPrint => Phase == EntryPhase.Preparation && Identity.IsComplete && !HasPrintedIdentity;
        public bool HackerCanReadRulesDuringPreparation => hackerCanReadRulesDuringPreparation;
        public bool LocalSimulation => localSimulation;
        public float PreparationSeconds => preparationSeconds;
        public event Action Changed;
        public event Action<IdentityField, string> IdentityEditRequested;
        public event Action StartRequested;
        public event Action PrintRequested;
        public event Action<IdentityCard> PreparationCompleted;

        // The online adapter selects network mode before activating the prefab.
        // The serialized offline-testing configuration remains unchanged on the asset.
        public void EnterNetworkMode() => localSimulation = false;

        private void Update()
        {
            if (!localSimulation || Phase != EntryPhase.Preparation) return;
            AdvanceLocalTime(Time.unscaledDeltaTime);
        }

        public void RequestStart()
        {
            if (Phase != EntryPhase.Waiting) return;
            if (localSimulation) BeginPreparation(preparationSeconds);
            else StartRequested?.Invoke();
        }

        public void BeginPreparation(float duration)
        {
            Identity = new IdentityCard();
            PrintedIdentity = new IdentityCard();
            HasPrintedIdentity = false;
            RemainingSeconds = Mathf.Max(0, duration);
            Phase = EntryPhase.Preparation;
            Changed?.Invoke();
            if (RemainingSeconds <= 0) FinishPreparation();
        }

        public void RequestIdentityEdit(IdentityField field, string value)
        {
            if (Phase != EntryPhase.Preparation || HasPrintedIdentity) return;
            if (localSimulation) ApplyIdentityEdit(field, value);
            else IdentityEditRequested?.Invoke(field, value);
        }

        // Call on the authority after validating that the sender is the hacker.
        public void ApplyIdentityEdit(IdentityField field, string value)
        {
            if (Phase != EntryPhase.Preparation || HasPrintedIdentity) return;
            var card = Identity;
            value = value ?? string.Empty;
            value = value.Substring(0, Mathf.Min(value.Length, 64));
            switch (field)
            {
                case IdentityField.Name: card.Name = value; break;
                case IdentityField.Nationality: card.Nationality = value; break;
                case IdentityField.Age: card.Age = value; break;
                case IdentityField.LicenseNumber: card.LicenseNumber = value; break;
                default: return;
            }
            Identity = card;
            Changed?.Invoke();
        }

        public void RequestPrint()
        {
            if (!CanPrint) return;
            if (localSimulation) PrintIdentity();
            else PrintRequested?.Invoke();
        }

        // Network authority validates the sender's hacker role before calling this.
        public void PrintIdentity()
        {
            if (!CanPrint) return;
            PrintedIdentity = Identity;
            HasPrintedIdentity = true;
            Changed?.Invoke();
        }

        public void ApplyPrintedIdentitySnapshot(bool hasPrinted, IdentityCard card)
        {
            if (localSimulation) return;
            HasPrintedIdentity = hasPrinted;
            PrintedIdentity = hasPrinted ? card : new IdentityCard();
            if (hasPrinted) Identity = card;
            Changed?.Invoke();
        }

        public void AdvanceLocalTime(float seconds)
        {
            if (!localSimulation || Phase != EntryPhase.Preparation || seconds < 0) return;
            RemainingSeconds = Mathf.Max(0, RemainingSeconds - seconds);
            if (RemainingSeconds <= 0) FinishPreparation();
            else Changed?.Invoke();
        }

        private void FinishPreparation()
        {
            if (Phase != EntryPhase.Preparation) return;
            RemainingSeconds = 0;
            Phase = EntryPhase.Interview;
            Changed?.Invoke();
            PreparationCompleted?.Invoke(Identity);
        }

        public void ApplyNetworkSnapshot(EntryPhase phase, float remaining, IdentityCard card)
        {
            ApplyNetworkSnapshot(phase, remaining, card, HasPrintedIdentity, PrintedIdentity);
        }

        // Apply the identity and its printed copy before notifying views or entering Interview.
        public void ApplyNetworkSnapshot(EntryPhase phase, float remaining, IdentityCard card,
            bool hasPrinted, IdentityCard printedCard)
        {
            if (localSimulation) return;
            bool completed = Phase != EntryPhase.Interview && phase == EntryPhase.Interview;
            Phase = phase;
            RemainingSeconds = Mathf.Max(0, remaining);
            Identity = card;
            HasPrintedIdentity = hasPrinted;
            PrintedIdentity = hasPrinted ? printedCard : new IdentityCard();
            Changed?.Invoke();
            if (completed) PreparationCompleted?.Invoke(hasPrinted ? printedCard : card);
        }
    }
}
