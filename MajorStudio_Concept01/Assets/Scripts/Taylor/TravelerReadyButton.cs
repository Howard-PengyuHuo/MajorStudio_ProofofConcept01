using UnityEngine;
using UnityEngine.UI;

namespace CarryOn.Taylor
{
    // Lives on the queue panel so hiding the button does not unsubscribe the view.
    public sealed class TravelerReadyButton : MonoBehaviour
    {
        [SerializeField] private PreparationSession session;
        [SerializeField] private Button readyButton;
        private bool requested;

        private void OnEnable()
        {
            requested = false;
            if (session != null) session.Changed += Refresh;
            if (readyButton != null) readyButton.onClick.AddListener(Ready);
            Refresh();
        }

        private void OnDisable()
        {
            if (session != null) session.Changed -= Refresh;
            if (readyButton != null) readyButton.onClick.RemoveListener(Ready);
        }

        private void Ready()
        {
            if (requested || session == null || !session.CanSkipQueue) return;
            requested = true;
            readyButton.interactable = false;
            session.RequestReady();
        }

        private void Refresh()
        {
            if (session == null || readyButton == null) return;
            if (!session.CanSkipQueue) requested = false;
            readyButton.gameObject.SetActive(session.CanSkipQueue);
            readyButton.interactable = !requested;
        }
    }
}
