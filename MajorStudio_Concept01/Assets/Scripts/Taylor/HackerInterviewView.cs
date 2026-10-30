using System.Collections;
using TMPro;
using UnityEngine;

namespace CarryOn.Taylor
{
    public class HackerInterviewView : MonoBehaviour
    {
        [SerializeField] private PreparationSession session;
        [SerializeField] private TMP_Text nameText, nationalityText, ageText, licenseText;
        [SerializeField] private InterviewController interview;
        [SerializeField] private TMP_Text currentQuestionText;
        [SerializeField] private RectTransform shakeRoot;
        [SerializeField, Min(0.01f)] private float shakeDuration = 1f;
        [SerializeField, Min(0f)] private float shakeStrength = 12f;
        private Coroutine shakeRoutine;
        private Vector2 restingPosition;

        private void OnEnable()
        {
            if (interview != null) interview.ShakePresented += PlayShake;
            RefreshQuestion();
            if (session == null) return;
            session.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (interview != null) interview.ShakePresented -= PlayShake;
            StopShake();
            if (session != null) session.Changed -= Refresh;
        }

        // Read the replicated question index after the network adapter has applied its snapshot.
        private void LateUpdate() => RefreshQuestion();

        private void RefreshQuestion()
        {
            if (currentQuestionText == null || interview == null) return;
            string question = interview.CurrentQuestion;
            if (currentQuestionText.text != question) currentQuestionText.text = question;
        }

        public void PlayShake()
        {
            if (!isActiveAndEnabled || shakeRoot == null || interview == null || interview.IsFinished) return;
            StopShake();
            restingPosition = shakeRoot.anchoredPosition;
            shakeRoutine = StartCoroutine(ShakeScreen());
        }

        private IEnumerator ShakeScreen()
        {
            float elapsed = 0f;
            while (elapsed < shakeDuration)
            {
                shakeRoot.anchoredPosition = restingPosition + Random.insideUnitCircle * shakeStrength;
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            shakeRoot.anchoredPosition = restingPosition;
            shakeRoutine = null;
        }

        private void StopShake()
        {
            if (shakeRoutine == null) return;
            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
            if (shakeRoot != null) shakeRoot.anchoredPosition = restingPosition;
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
