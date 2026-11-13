using System;
using UnityEngine;

namespace Game.Multiplayer
{
    [RequireComponent(typeof(AudioSource), typeof(LobbyRoleSelection))]
    public sealed class SynchronizedBackgroundMusic : MonoBehaviour
    {
        private AudioSource source;
        private LobbyRoleSelection selection;
        private double startedAt = double.NaN;
        private float nextCheck;
        private void Awake()
        {
            source = GetComponent<AudioSource>();
            selection = GetComponent<LobbyRoleSelection>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0;
        }

        private void Update()
        {
            if (!selection.IsSpawned || !selection.Current.MusicPlaying ||
                source.clip == null || source.clip.length <= 0)
            {
                if (!double.IsNaN(startedAt)) source.Stop();
                startedAt = double.NaN;
                return;
            }
            double now = selection.ServerNow;
            double start = selection.Current.MusicStartedAt;
            if (startedAt != start)
            {
                source.Stop();
                startedAt = start;
                // Schedule against a shared server epoch; late clients seek to the same position.
                double delay = Math.Max(.1, start - now);
                source.time = (float)(Math.Max(0, now + delay - start) % source.clip.length);
                source.PlayScheduled(AudioSettings.dspTime + delay);
                nextCheck = Time.unscaledTime + (float)delay + 2;
            }
            if (Time.unscaledTime < nextCheck || now < start) return;
            nextCheck = Time.unscaledTime + 2;
            float expected = (float)((now - start) % source.clip.length);
            float difference = Mathf.Abs(source.time - expected);
            difference = Mathf.Min(difference, source.clip.length - difference);
            if (!source.isPlaying || difference > .3f)
            {
                source.time = expected;
                if (!source.isPlaying) source.Play();
            }
        }

        private void OnDisable()
        {
            if (source != null) source.Stop();
            startedAt = double.NaN;
        }
    }
}
