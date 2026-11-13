using UnityEngine;

namespace CarryOn.Taylor
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class OfficerClosingSfx : MonoBehaviour
    {
        [SerializeField] private InterviewController interview;
        [SerializeField] private AudioClip clack;
        private AudioSource source;
        private bool played;

        private void Awake() => source = GetComponent<AudioSource>();
        private void OnEnable() => played = false;

        private void LateUpdate()
        {
            if (interview == null) return;
            // This state covers both the approval and denial closing lines.
            if (!interview.IsExplainingDenial)
            {
                played = false;
                return;
            }
            if (played) return;
            played = true;
            if (clack != null) source.PlayOneShot(clack);
        }

        private void OnDisable() { if (source != null) source.Stop(); }
    }
}
