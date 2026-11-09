#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Game.Multiplayer.Editor
{
    /// <summary>Wires the authored prefab without generating or replacing any visual elements.</summary>
    public static class CutsceneSetup
    {
        public const string PrefabPath = "Assets/Prefabs/MultiplayerCutscene.prefab";

        [MenuItem("Tools/Multiplayer Cutscene/Configure Assets")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode first.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || prefab.GetComponent<CutsceneCarousel>() == null)
                throw new InvalidOperationException("Restore the authored MultiplayerCutscene prefab before configuring.");
            var role = PrefabUtility.LoadPrefabContents(LobbyRoleSelectionSetup.RolePrefabPath);
            try
            {
                var serialized = new SerializedObject(role.GetComponent<LobbyRoleSelection>());
                serialized.FindProperty("cutscenePrefab").objectReferenceValue = prefab.GetComponent<CutsceneCarousel>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(role, LobbyRoleSelectionSetup.RolePrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(role); }
            AssetDatabase.SaveAssets();
            Debug.Log("Existing cutscene prefab assigned. No UI objects, artwork or layouts were recreated.");
        }
    }
}
#endif
