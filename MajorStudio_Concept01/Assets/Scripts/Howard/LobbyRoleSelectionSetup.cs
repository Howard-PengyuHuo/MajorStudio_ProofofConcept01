#if UNITY_EDITOR
using UnityEditor;

namespace Game.Multiplayer.Editor
{
    public static class LobbyRoleSelectionSetup
    {
        public const string RolePrefabPath = "Assets/Prefabs/LobbyRoleSelection.prefab";

        // Retain the old menu entry without recreating the superseded role/Ready sidebar.
        [MenuItem("Tools/Role Selection/Configure Assets")]
        public static void Configure() => CutsceneSetup.Configure();
    }
}
#endif
