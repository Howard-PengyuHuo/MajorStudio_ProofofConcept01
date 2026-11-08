using UnityEngine;

namespace CarryOn.Taylor
{
    internal static class GamePerformanceSettings
    {
        // This UI game shares the GPU with local Ollama. Avoid unlimited rendering in builds.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Configure() => Application.targetFrameRate = 60;
    }
}
