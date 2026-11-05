using System;

namespace CarryOn.Taylor
{
    public enum TaskCategory { None, Politics, CulturalConnection, BusinessCollaboration, OtherCategory }
    public enum GovernmentField { Name, Nationality, Position, Category }

    [Serializable]
    public struct GovernmentDocument
    {
        public string Name, Nationality, Position;
        public TaskCategory Category;
        public bool IsComplete => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Nationality) &&
            !string.IsNullOrWhiteSpace(Position) && Category >= TaskCategory.Politics && Category <= TaskCategory.OtherCategory;
        public static string CategoryLabel(TaskCategory value) => value switch
        {
            TaskCategory.Politics => "Politics",
            TaskCategory.CulturalConnection => "Cultural Connection",
            TaskCategory.BusinessCollaboration => "Business Collaboration",
            TaskCategory.OtherCategory => "Other Category",
            _ => ""
        };
    }

    public partial class PreparationSession
    {
        public GovernmentDocument Government { get; private set; }
        public GovernmentDocument PrintedGovernment { get; private set; }
        public bool HasPrintedGovernment { get; private set; }
        public bool CanPrintGovernment => Phase == EntryPhase.Preparation && !HasPrintedGovernment && Government.IsComplete;
        public event Action<GovernmentField, string> GovernmentEditRequested;
        public event Action GovernmentPrintRequested;

        public void RequestGovernmentEdit(GovernmentField field, string value)
        {
            if (Phase != EntryPhase.Preparation || HasPrintedGovernment) return;
            if (LocalSimulation) ApplyGovernmentEdit(field, value);
            else GovernmentEditRequested?.Invoke(field, value);
        }

        public void ApplyGovernmentEdit(GovernmentField field, string value)
        {
            if (Phase != EntryPhase.Preparation || HasPrintedGovernment) return;
            var card = Government;
            value ??= "";
            if (value.Length > 64) value = value.Substring(0, 64);
            switch (field)
            {
                case GovernmentField.Name: card.Name = value; break;
                case GovernmentField.Nationality: card.Nationality = value; break;
                case GovernmentField.Position: card.Position = value; break;
                case GovernmentField.Category:
                    if (!int.TryParse(value, out int category) || category < 1 || category > 4) return;
                    card.Category = (TaskCategory)category;
                    break;
                default: return;
            }
            Government = card;
            Changed?.Invoke();
        }

        public void RequestGovernmentPrint()
        {
            if (!CanPrintGovernment) return;
            if (LocalSimulation) PrintGovernment();
            else GovernmentPrintRequested?.Invoke();
        }

        public void PrintGovernment()
        {
            if (!CanPrintGovernment) return;
            PrintedGovernment = Government;
            HasPrintedGovernment = true;
            Changed?.Invoke();
        }

        // Applied before the regular snapshot raises Changed, so both documents arrive together.
        public void ApplyGovernmentSnapshot(GovernmentDocument draft, bool printed, GovernmentDocument copy)
        {
            if (LocalSimulation) return;
            Government = draft;
            HasPrintedGovernment = printed;
            PrintedGovernment = copy;
        }
    }
}
