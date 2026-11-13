using UnityEngine;

namespace CarryOn.Taylor
{
    public sealed class TravelerQueueNote : MonoBehaviour
    {
        [SerializeField] private PreparationSession session;
        [SerializeField] private GameObject note;
        [SerializeField, Min(0)] private float printDelay = 2f;

        private void OnEnable() => Refresh();
        private void Update() => Refresh();

        private void Refresh()
        {
            if (session == null || note == null) return;
            // Use the replicated queue clock, so opening a panel does not restart the delay.
            bool visible = (session.Phase == EntryPhase.Preparation &&
                session.PreparationSeconds - session.RemainingSeconds >= printDelay) ||
                session.Phase == EntryPhase.CalledToCounter;
            if (note.activeSelf == visible) return;
            if (visible) note.transform.SetAsLastSibling();
            note.SetActive(visible);
        }
    }
}
