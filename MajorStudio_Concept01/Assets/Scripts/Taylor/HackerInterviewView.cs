using TMPro;
using UnityEngine;

namespace CarryOn.Taylor
{
    public class HackerInterviewView : MonoBehaviour
    {
        [SerializeField] private PreparationSession session;
        [SerializeField] private TMP_Text nameText, nationalityText, ageText, licenseText;

        private void OnEnable()
        {
            if (session == null) return;
            session.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (session != null) session.Changed -= Refresh;
        }

        private void Refresh()
        {
            var card = session.HasPrintedIdentity ? session.PrintedIdentity : session.Identity;
            if (nameText != null) nameText.text = card.Name ?? "";
            if (nationalityText != null) nationalityText.text = card.Nationality ?? "";
            if (ageText != null) ageText.text = card.Age ?? "";
            if (licenseText != null) licenseText.text = card.LicenseNumber ?? "";
        }
    }
}
