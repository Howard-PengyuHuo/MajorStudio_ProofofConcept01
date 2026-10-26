#if UNITY_EDITOR
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Multiplayer.Editor
{
    // Explicit, idempotent authoring command. It never runs on import or changes gameplay UI assets.
    public static class CoopGameSceneSetup
    {
        private const string GameplayPrefabPath = "Assets/Prefabs/Taylor/Canvas.prefab";
        private const string LobbyPrefabPath = "Assets/Prefabs/MultiplayerCanvas.prefab";
        public const string GameScenePath = "Assets/Scenes/GameScene.unity";
        public const string BridgePrefabPath = "Assets/Prefabs/CooperativeGameState.prefab";

        [MenuItem("Tools/Cooperative Game/Configure Integration Assets")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Exit Play Mode before configuring integration assets.");
            GameObject gameplay = AssetDatabase.LoadAssetAtPath<GameObject>(GameplayPrefabPath);
            if (gameplay == null) throw new System.InvalidOperationException("Existing gameplay Canvas prefab was not found.");
            ConfigureStartButton();
            var scene = EditorSceneManager.GetSceneByPath(GameScenePath);
            bool openedHere = !scene.IsValid();
            if (openedHere)
                scene = System.IO.File.Exists(GameScenePath)
                    ? EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var bridge = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CoopGameNetworkBridge>(true))
                .SingleOrDefault();
            if (bridge == null)
            {
                var root = new GameObject("CooperativeGameState", typeof(NetworkObject));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                bridge = root.AddComponent<CoopGameNetworkBridge>();
            }
            var serialized = new SerializedObject(bridge);
            serialized.FindProperty("gameplayCanvasPrefab").objectReferenceValue = gameplay;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            bridge.GetComponent<NetworkObject>().SynchronizeTransform = false;
            PrefabUtility.SaveAsPrefabAssetAndConnect(bridge.gameObject, BridgePrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene, GameScenePath);
            var buildScenes = EditorBuildSettings.scenes.ToList();
            int index = buildScenes.FindIndex(item => item.path == GameScenePath);
            if (index < 0) buildScenes.Add(new EditorBuildSettingsScene(GameScenePath, true));
            else buildScenes[index] = new EditorBuildSettingsScene(GameScenePath, true);
            EditorBuildSettings.scenes = buildScenes.ToArray();
            // Now that the scene has a GUID/build entry, let NGO generate its scene-object hash.
            bridge.GetComponent<NetworkObject>().SendMessage("OnValidate", SendMessageOptions.DontRequireReceiver);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, GameScenePath);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            Debug.Log("Cooperative game integration assets configured. Gameplay Canvas prefab layout was not modified.");
        }

        private static void ConfigureStartButton()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                Transform panel = root.transform.Find("LobbyPanel");
                if (panel == null) throw new System.InvalidOperationException("LobbyPanel was not found in the lobby prefab.");
                if (panel.Find("StartGameButton") != null) return;
                var template = panel.Find("LeaveRoomButton");
                if (template == null) throw new System.InvalidOperationException("Existing LeaveRoomButton template was not found.");
                GameObject button = Object.Instantiate(template.gameObject, panel, false);
                button.name = "StartGameButton";
                button.transform.SetSiblingIndex(template.GetSiblingIndex());
                button.GetComponentInChildren<Text>(true).text = "START GAME";
                button.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
                PrefabUtility.SaveAsPrefabAsset(root, LobbyPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
#endif
