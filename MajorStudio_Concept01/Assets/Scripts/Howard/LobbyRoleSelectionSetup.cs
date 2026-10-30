#if UNITY_EDITOR
using System;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Multiplayer.Editor
{
    public static class LobbyRoleSelectionSetup
    {
        public const string RolePrefabPath = "Assets/Prefabs/LobbyRoleSelection.prefab";
        private const string LobbyPrefabPath = "Assets/Prefabs/MultiplayerCanvas.prefab";
        private const string LobbyScenePath = "Assets/Scenes/MultiplayerTest.unity";

        [MenuItem("Tools/Role Selection/Configure Assets")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before configuring role selection.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RolePrefabPath);
            if (prefab == null)
            {
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var root = new GameObject("LobbyRoleSelection", typeof(NetworkObject), typeof(LobbyRoleSelection));
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                    var networkObject = root.GetComponent<NetworkObject>();
                    networkObject.SynchronizeTransform = false;
                    networkObject.SceneMigrationSynchronization = false;
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, RolePrefabPath);
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            if (!list.Contains(prefab)) list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            ConfigureLobbyUI();
            ConfigureSessionReference(prefab.GetComponent<LobbyRoleSelection>());
            var gameplay = PrefabUtility.LoadPrefabContents(CoopGameSceneSetup.BridgePrefabPath);
            try
            {
                var serialized = new SerializedObject(gameplay.GetComponent<CoopGameNetworkBridge>());
                serialized.FindProperty("gameplaySynchronizationEnabled").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(gameplay, CoopGameSceneSetup.BridgePrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(gameplay); }
            AssetDatabase.SaveAssets();
            Debug.Log("Role selection configured. Existing gameplay Canvas/layout and original Lobby controls retained.");
        }

        private static void ConfigureSessionReference(LobbyRoleSelection prefab)
        {
            var scene = EditorSceneManager.GetSceneByPath(LobbyScenePath);
            bool openedHere = !scene.IsValid();
            if (openedHere) scene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Additive);
            try
            {
                var manager = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NetworkSessionManager>(true)).Single();
                var serialized = new SerializedObject(manager);
                serialized.FindProperty("lobbyRoleSelectionPrefab").objectReferenceValue = prefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
        }

        private static void ConfigureLobbyUI()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                if (root.transform.Find("RoleSelectionPanel") != null) return;
                var template = root.transform.Find("LobbyPanel");
                var panel = UnityEngine.Object.Instantiate(template.gameObject, root.transform, false);
                panel.name = "RoleSelectionPanel";
                foreach (Transform child in panel.transform.Cast<Transform>().ToArray())
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                // Only the new sidebar has new layout values. Original LobbyPanel remains unchanged.
                var rect = panel.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(-470, 0);
                rect.sizeDelta = new Vector2(300, 620);
                var layout = panel.GetComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(20, 20, 20, 20);
                layout.spacing = 10;
                CopyText(template.Find("LobbyTitle"), panel.transform, "RoleSelectionTitle", "CHOOSE YOUR ROLE", 22, 48);
                CopyText(template.Find("PlayerStatusText"), panel.transform, "RoleAssignmentsText",
                    "Host: Traveler - Not Ready\n\nClient: Waiting...", 16, 130);
                CopyButton(template.Find("LeaveRoomButton"), panel.transform, "TravelerRoleButton", "TRAVELER");
                CopyButton(template.Find("LeaveRoomButton"), panel.transform, "HackerRoleButton", "HACKER");
                CopyButton(template.Find("LeaveRoomButton"), panel.transform, "ReleaseRoleButton", "RELEASE ROLE");
                CopyButton(template.Find("LeaveRoomButton"), panel.transform, "ReadyButton", "READY");
                CopyText(template.Find("PlayerStatusText"), panel.transform, "RoleSelectionStatus",
                    "Choose a role, then Ready. Release your role to swap.", 16, 110);
                panel.SetActive(true);
                PrefabUtility.SaveAsPrefabAsset(root, LobbyPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void CopyText(Transform template, Transform parent, string objectName, string text, int fontSize, float height)
        {
            var copy = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            copy.name = objectName;
            copy.GetComponent<Text>().text = text;
            copy.GetComponent<Text>().fontSize = fontSize;
            copy.GetComponent<LayoutElement>().preferredHeight = height;
        }

        private static void CopyButton(Transform template, Transform parent, string objectName, string label)
        {
            var copy = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            copy.name = objectName;
            copy.GetComponentInChildren<Text>(true).text = label;
            copy.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
            copy.GetComponent<LayoutElement>().preferredHeight = 46;
        }
    }
}
#endif
