using System.Collections;
using TMPro;
using UnityEngine;

namespace CarryOn.Taylor
{
    public class HackerInterviewView : MonoBehaviour
    {
        [SerializeField] private PreparationSession session;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text lastNameText;
        [UnityEngine.Serialization.FormerlySerializedAs("nationalityText"), SerializeField] private TMP_Text birthPlaceText;
        [UnityEngine.Serialization.FormerlySerializedAs("ageText"), SerializeField] private TMP_Text issueDateText;
        [UnityEngine.Serialization.FormerlySerializedAs("licenseText"), SerializeField] private TMP_Text passportCodeText;
        [SerializeField] private TMP_Text expiryDateText;
        [SerializeField] private TMP_Text birthdayText;
        [SerializeField] private InterviewController interview;
        [SerializeField] private TMP_Text currentQuestionText;
        [SerializeField] private RectTransform shakeRoot;
        [SerializeField, Min(0.01f)] private float shakeDuration = 1f;
        [SerializeField, Min(0f)] private float shakeStrength = 12f;
        private Coroutine shakeRoutine;
        private Vector2 restingPosition;
        private string lastQuestion;
        private string lastAnswer;

        private void OnEnable()
        {
            lastQuestion = null;
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
            string answer = interview.ReviewingPurpose && !interview.IsFinished && !interview.IsExplainingDenial
                ? interview.LastSubmittedAnswer : string.Empty;
            if (lastQuestion == question && lastAnswer == answer) return;
            lastQuestion = question;
            lastAnswer = answer;
            // Player text must be shown literally, not interpreted as TMP formatting.
            currentQuestionText.richText = false;
            currentQuestionText.text = !string.IsNullOrWhiteSpace(answer) ? "Your partner answered:\n" + answer :
                string.IsNullOrWhiteSpace(question) ? string.Empty :
                "The Traveler is asked:\n" + question;
        }

        public void PlayShake()
        {
            if (!isActiveAndEnabled || shakeRoot == null || interview == null || interview.IsFinished) return;
            StopShake();
            restingPosition = shakeRoot.anchoredPosition;
            foreach (var document in GetComponentsInChildren<LooseDocumentMotion>())
                document.Shake(shakeDuration);
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
            if (nameText != null) nameText.text = (lastNameText != null ? card.FirstName : card.Name) ?? "";
            if (lastNameText != null) lastNameText.text = card.LastName ?? "";
            if (birthPlaceText != null) birthPlaceText.text = card.BirthPlace ?? "";
            if (issueDateText != null) issueDateText.text = card.IssueDate ?? "";
            if (expiryDateText != null) expiryDateText.text = card.ExpiryDate ?? "";
            if (birthdayText != null) birthdayText.text = card.Birthday ?? "";
            if (passportCodeText != null) passportCodeText.text = card.PassportCode ?? "";
        }
    }
}
