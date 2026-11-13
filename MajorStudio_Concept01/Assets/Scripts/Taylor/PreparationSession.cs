using System;
using UnityEngine;

namespace CarryOn.Taylor
{
    public enum EntryPhase { Waiting, Preparation, Interview, CalledToCounter }
    public enum PlayerRole { Traveler, Hacker }
    public enum IdentityField { Name, BirthPlace, IssueDate, PassportCode, ExpiryDate, Birthday, FirstName, LastName }

    [Serializable]
    public struct IdentityCard
    {
        // Birthday retains its serialized/network name, but now stores a four-digit birth year.
        public string FirstName, LastName;
        public string Name, BirthPlace, IssueDate, PassportCode, ExpiryDate, Birthday;
        // Keep the legacy storage/wire field for existing prefabs and network serialization.
        public string Nationality { get => BirthPlace; set => BirthPlace = value; }
        public const string FixedIssueDate = "1976-01-21";
        public const string FixedExpiryDate = "1980-01-21";
        public bool IsComplete => !string.IsNullOrWhiteSpace(Name) &&
            ((string.IsNullOrEmpty(FirstName) && string.IsNullOrEmpty(LastName)) ||
             (!string.IsNullOrWhiteSpace(FirstName) && !string.IsNullOrWhiteSpace(LastName))) &&
            !string.IsNullOrWhiteSpace(Nationality) &&
            !string.IsNullOrWhiteSpace(PassportCode) &&
            !string.IsNullOrWhiteSpace(Birthday);
    }

    /// <summary>Local prototype authority. A network adapter can disable local timing and apply snapshots.</summary>
    public partial class PreparationSession : MonoBehaviour
    {
        [SerializeField, Min(1)] private float preparationSeconds = 150;
        public const float CounterCallSeconds = 2;
        public int PeopleAhead
        {
            get
            {
                if (Phase == EntryPhase.Waiting) return 6;
                if (Phase != EntryPhase.Preparation) return 0;
                float elapsed = 150 * (1 - RemainingSeconds / PreparationSeconds);
                return elapsed < 18 ? 6 : elapsed < 49 ? 5 : elapsed < 71 ? 4 :
                    elapsed < 105 ? 3 : elapsed < 124 ? 2 : elapsed < 150 ? 1 : 0;
            }
        }
        [SerializeField] private bool localSimulation = true;
        [SerializeField] private bool hackerCanReadRulesDuringPreparation;
        public EntryPhase Phase { get; private set; }
        public float RemainingSeconds { get; private set; }
        public IdentityCard Identity { get; private set; }
        public IdentityCard PrintedIdentity { get; private set; }
        public bool HasPrintedIdentity { get; private set; }
        public bool CanPrint => Phase == EntryPhase.Preparation && Identity.IsComplete && !HasPrintedIdentity &&
            PassportDate.TryNormalizeCard(Identity, out _);
        public bool HackerCanReadRulesDuringPreparation => hackerCanReadRulesDuringPreparation;
        public bool LocalSimulation => localSimulation;
        public float PreparationSeconds => preparationSeconds;
        public event Action Changed;
        public event Action<IdentityField, string> IdentityEditRequested;
        public event Action StartRequested;
        public event Action ReadyRequested;
        public bool CanSkipQueue => Phase == EntryPhase.Preparation && HasPrintedIdentity && HasPrintedGovernment;

        public void RequestReady()
        {
            if (!CanSkipQueue) return;
            if (localSimulation) FinishPreparation();
            else ReadyRequested?.Invoke();
        }
        public event Action PrintRequested;
        public event Action<IdentityCard> PreparationCompleted;

        // The online adapter selects network mode before activating the prefab.
        // The serialized offline-testing configuration remains unchanged on the asset.
        public void EnterNetworkMode() => localSimulation = false;

        private void Update()
        {
            if (!localSimulation || (Phase != EntryPhase.Preparation && Phase != EntryPhase.CalledToCounter)) return;
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
            Government = default;
            PrintedGovernment = default;
            HasPrintedGovernment = false;
            Identity = new IdentityCard { IssueDate = IdentityCard.FixedIssueDate, ExpiryDate = IdentityCard.FixedExpiryDate };
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
                case IdentityField.Name: card.Name = value; card.FirstName = card.LastName = null; break;
                case IdentityField.FirstName: card.FirstName = value; card.Name = CombineName(card.FirstName, card.LastName); break;
                case IdentityField.LastName: card.LastName = value; card.Name = CombineName(card.FirstName, card.LastName); break;
                case IdentityField.BirthPlace: card.BirthPlace = value; break;
                case IdentityField.IssueDate: return; // Printed on the new passport artwork.
                case IdentityField.PassportCode: card.PassportCode = value; break;
                case IdentityField.ExpiryDate: return;
                case IdentityField.Birthday: card.Birthday = value; break;
                default: return;
            }
            Identity = card;
            Changed?.Invoke();
        }

        public static string CombineName(string first, string last) => ((first ?? "").Trim() + " " + (last ?? "").Trim()).Trim();

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
            if (!PassportDate.TryNormalizeCard(Identity, out var normalized)) return;
            Identity = normalized;
            PrintedIdentity = normalized;
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
            if (!localSimulation || (Phase != EntryPhase.Preparation && Phase != EntryPhase.CalledToCounter) || seconds < 0) return;
            int previousSecond = Mathf.CeilToInt(RemainingSeconds);
            RemainingSeconds = Mathf.Max(0, RemainingSeconds - seconds);
            if (RemainingSeconds <= 0) FinishPreparation();
            else if (previousSecond != Mathf.CeilToInt(RemainingSeconds)) Changed?.Invoke();
        }

        private void FinishPreparation()
        {
            if (Phase == EntryPhase.Preparation)
            {
                Phase = EntryPhase.CalledToCounter;
                RemainingSeconds = CounterCallSeconds;
                Changed?.Invoke();
                return;
            }
            if (Phase != EntryPhase.CalledToCounter) return;
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
