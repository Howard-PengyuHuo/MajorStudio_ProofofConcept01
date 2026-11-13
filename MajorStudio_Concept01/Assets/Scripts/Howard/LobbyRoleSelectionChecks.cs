#if UNITY_EDITOR
using System;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using CarryOn.Taylor;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Game.Multiplayer.Editor
{
    public static class LobbyRoleSelectionChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static bool running;
        private static int checks;
        private static string evidenceFolder;

        [MenuItem("Tools/Multiplayer Cutscene/Run Loopback Checks (Play Mode)")]
        [MenuItem("Tools/Role Selection/Run Loopback Checks (Play Mode)")]
        public static async void Run()
        {
            var host = NetworkManager.Singleton;
            if (!EditorApplication.isPlaying || running || host == null || host.IsListening)
            {
                Debug.LogWarning("Enter Play Mode in an idle MultiplayerTest lobby first.");
                return;
            }
            running = true;
            checks = 0;
            evidenceFolder = Path.Combine(Path.GetTempPath(), "character-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidenceFolder);
            var rolePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyRoleSelectionSetup.RolePrefabPath);
            var gamePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CoopGameSceneSetup.BridgePrefabPath);
            var hostSession = host.GetComponent<NetworkSessionManager>();
            NetworkManager client = null;
            CoopGameNetworkBridge game = null;
            bool originalScenes = host.NetworkConfig.EnableSceneManagement;
            bool originalSame = host.NetworkConfig.ForceSamePrefabs;
            var transport = host.GetComponent<UnityTransport>();
            var protocolField = typeof(UnityTransport).GetField("m_ProtocolType", Private);
            object originalProtocol = protocolField.GetValue(transport);
            try
            {
                host.NetworkConfig.EnableSceneManagement = false;
                host.NetworkConfig.ForceSamePrefabs = false;
                protocolField.SetValue(transport, UnityTransport.ProtocolType.UnityTransport);
                transport.SetConnectionData("127.0.0.1", 17984);
                Check(host.StartHost(), "Host transport started");
                Call(hostSession, "SpawnRoleSelection");
                Call(hostSession, "SetState", ConnectionState.Connected, "Cutscene check fixture");
                await CheckLobbyArtwork(hostSession);
                var roles = hostSession.RoleSelection;
                Check(!hostSession.CanStartGame && roles.Current.Host.Role == LobbyRole.Unassigned, "One player cannot start; no Lobby role assigned");
                var hostView = roles.GetComponentInChildren<CutsceneCarousel>();
                Set(hostView, "transitionDuration", 0.1f);
                foreach (var panel in Get<CutsceneCarousel.ComicPanel[]>(hostView, "comics"))
                {
                    panel.postNarrationHoldDuration = 0.1f;
                    foreach (var segment in panel.narration) segment.duration = 0.25f;
                }
                Set(hostView, "selectionScaleDuration", 0.2f);
                Set(roles, "countdownDuration", 1f);
                CheckCarouselModes(hostView);
                var clientObject = new GameObject("CutsceneCheckClient");
                clientObject.SetActive(false);
                var clientTransport = clientObject.AddComponent<UnityTransport>();
                client = clientObject.AddComponent<NetworkManager>();
                client.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = clientTransport, EnableSceneManagement = false,
                    ConnectionApproval = true, ForceSamePrefabs = false
                };
                var clientSession = clientObject.AddComponent<NetworkSessionManager>();
                clientObject.SetActive(true);
                clientTransport.SetConnectionData("127.0.0.1", 17984);
                if (!client.NetworkConfig.Prefabs.Contains(rolePrefab)) client.AddNetworkPrefab(rolePrefab);
                if (!client.NetworkConfig.Prefabs.Contains(gamePrefab)) client.AddNetworkPrefab(gamePrefab);
                Check(client.StartClient(), "Client transport started");
                await Until(() => client.IsConnectedClient && clientSession.RoleSelection != null &&
                    clientSession.RoleSelection.Current.Client.Connected, "Client connected with authoritative slots");
                var remote = clientSession.RoleSelection;
                var clientView = remote.GetComponentInChildren<CutsceneCarousel>();
                CheckLobbyBackgrounds(hostSession, clientSession);
                Check(hostSession.CanStartGame && !clientSession.CanStartGame, "Two players enables only Host Start without Ready");
                Check(roles.Current.Client.Role == LobbyRole.Unassigned, "Client has no default role");
                remote.SelectRole(LobbyRole.Traveler);
                await Confirm(remote);
                Check(roles.Current.Client.Role == LobbyRole.Unassigned, "Lobby role requests rejected");
                clientSession.StartGame();
                Check(roles.Current.Stage == SessionStage.Lobby, "Client cannot start introduction");
                hostSession.StartGame();
                uint firstSequence = roles.Current.ComicSequence;
                hostSession.StartGame();
                Check(roles.Current.ComicSequence == firstSequence && !hostSession.IsInGame, "Double Start begins one introduction, not gameplay");
                await CheckManualPages(roles, remote, hostView, clientView);
                Check(roles.Current.MusicPlaying && remote.Current.MusicPlaying && roles.Current.MusicStartedAt == remote.Current.MusicStartedAt,
                    "BGM shares server epoch on both peers");
                await Task.Delay(800);
                var hostMusic = roles.GetComponent<AudioSource>();
                var clientMusic = remote.GetComponent<AudioSource>();
                Check(hostMusic.isPlaying && clientMusic.isPlaying && Mathf.Abs(hostMusic.time - clientMusic.time) < .35f,
                    "Both music sources play in sync");
                Check(hostMusic.loop && hostMusic.volume == rolePrefab.GetComponent<AudioSource>().volume, "BGM loops at authored volume");
                Check(hostView.FrameRectTransform.localScale == Vector3.one && clientView.FrameRectTransform.localScale == Vector3.one &&
                    Get<CutsceneCarousel.ComicPanel[]>(clientView, "comics")[2].image.rectTransform.localScale == Vector3.one,
                    "Final frame and Comic 3 reach exactly one on both peers");
                Check(Get<CutsceneCarousel.ComicPanel[]>(hostView, "comics")[0].image.rectTransform.localScale == Vector3.one * 0.8f,
                    "Comic 1 retains its authored reduced scale");
                Check(roles.Current.ComicIndex == 2 && !roles.Current.CountdownActive && !hostSession.IsInGame,
                    "Final comic waits for distinct selections; no premature gameplay");
                CheckLocalHover(hostView, clientView, roles);
                await CheckClickOwnership(hostView, clientView, roles, remote);
                roles.SelectRole(LobbyRole.Traveler);
                remote.SelectRole(LobbyRole.Traveler);
                await Confirm(remote);
                Check(roles.Current.Host.Role == LobbyRole.Traveler && roles.Current.Client.Role == LobbyRole.Unassigned,
                    "Competing character request rejected");
                remote.SelectRole(LobbyRole.Hacker);
                await Confirm(remote);
                await Until(() => remote.Current.CountdownActive, "Distinct selections automatically start synchronized countdown");
                uint generation = roles.Current.CountdownGeneration;
                double deadline = roles.Current.CountdownEndsAt;
                Check(remote.Current.CountdownEndsAt == deadline, "Both peers share exact server timestamp");
                Check(Get<Image>(hostView, "personMarker").sprite == Get<Sprite>(hostView, "playerOneMarkerSprite") &&
                    Get<Image>(clientView, "briefcaseMarker").sprite == Get<Sprite>(clientView, "playerTwoMarkerSprite"),
                    "P1/P2 marker images use authoritative slots");
                Check(Get<Image>(clientView, "stopwatchImage").gameObject.activeInHierarchy, "Stopwatch visible during countdown");
                roles.SelectRole(LobbyRole.Hacker);
                await Confirm(roles);
                Check(roles.Current.CountdownGeneration == generation && roles.Current.CountdownEndsAt == deadline,
                    "Occupied-role rejection preserves active countdown");
                roles.ReleaseRole();
                await Confirm(roles);
                await Until(() => !remote.Current.CountdownActive, "Accepted deselection cancels on both peers");
                Check(!Get<Image>(clientView, "stopwatchImage").gameObject.activeInHierarchy, "Stopwatch hidden after cancellation");
                Check(!roles.TryLockForStart(generation), "Canceled countdown cannot start gameplay");
                remote.ReleaseRole();
                await Confirm(remote);
                roles.SelectRole(LobbyRole.Hacker);
                remote.SelectRole(LobbyRole.Traveler);
                await Confirm(remote);
                await Until(() => remote.Current.CountdownActive, "Swapping roles starts a fresh countdown");
                Check(roles.Current.CountdownGeneration > generation && roles.Current.CountdownEndsAt > deadline &&
                    roles.Current.CountdownEndsAt - roles.ServerNow > 0.6, "Restart uses full duration and new generation");
                remote.SelectRole((LobbyRole)255);
                await Confirm(remote);
                Check(roles.Current.Client.Role == LobbyRole.Traveler, "Invalid role rejected");
                // Prevent scene loading only in this same-process transport fixture; separate-player Relay test covers it.
                Set(roles, "session", null);
                uint validGeneration = roles.Current.CountdownGeneration;
                Check(!roles.TryLockForStart(validGeneration), "Cannot start before deadline");
                await Until(() => roles.ServerNow >= roles.Current.CountdownEndsAt, "Server deadline elapsed");
                Check(!roles.TryLockForStart(generation), "Old generation still rejected after newer deadline");
                Check(roles.TryLockForStart(validGeneration) && !roles.TryLockForStart(validGeneration), "Latest valid expiry locks exactly once");
                Set(roles, "session", hostSession);
                remote.ReleaseRole();
                await Confirm(remote);
                Check(roles.Current.Client.Role == LobbyRole.Traveler, "Selection locked only after gameplay starts");
                game = UnityEngine.Object.Instantiate(gamePrefab).GetComponent<CoopGameNetworkBridge>();
                game.GetComponent<NetworkObject>().Spawn();
                CoopGameNetworkBridge remoteGame = null;
                await Until(() =>
                {
                    foreach (var bridge in UnityEngine.Object.FindObjectsByType<CoopGameNetworkBridge>())
                        if (bridge.IsSpawned && bridge.NetworkManager == client) remoteGame = bridge;
                    return remoteGame != null && remoteGame.GetComponentInChildren<Canvas>() != null;
                }, "Client gameplay presentation spawned");
                Check(game.LocalRole == PlayerRole.Hacker && remoteGame.LocalRole == PlayerRole.Traveler,
                    "Briefcase maps to Hacker, Person to Traveler independent of Host/Client");
                Check(Get<GameObject>(game.GetComponentInChildren<PreparationPreview>(true), "hacker").activeInHierarchy &&
                    Get<GameObject>(remoteGame.GetComponentInChildren<PreparationPreview>(true), "traveler").activeInHierarchy,
                    "Existing PreparationPreview uses selected roles");
                await CheckEndScreensAndRestart(game, remoteGame);
                await Task.Delay(100);
                Check(!roles.Current.MusicPlaying && !remote.Current.MusicPlaying && !hostMusic.isPlaying && !clientMusic.isPlaying,
                    "Ending stops BGM on both peers");
                game.NetworkObject.Despawn();
                roles.ReturnToCharacterSelection();
                await Until(() => remote.Current.Stage == SessionStage.CharacterSelection && !remote.Current.GameStarted,
                    "Restart destination synchronized to character selection");
                Check(roles.Current.Host.Role == LobbyRole.Unassigned && roles.Current.Client.Role == LobbyRole.Unassigned &&
                    !roles.Current.CountdownActive, "Restart clears both roles and old countdown");
                await Task.Delay(700);
                Check(roles.Current.MusicPlaying && remote.Current.MusicPlaying && hostMusic.isPlaying && clientMusic.isPlaying,
                    "Restart resumes BGM at role selection");
                Check(Get<Button>(hostView, "personButton").interactable && Get<Button>(clientView, "briefcaseButton").interactable,
                    "Both players can choose again without replaying comics");
                roles.ReturnToLobby();
                await Until(() => remote.Current.Stage == SessionStage.Lobby, "Return to Lobby resets introduction and roles");
                Check(hostSession.CanStartGame && roles.Current.Host.Role == LobbyRole.Unassigned, "Two connected players can start again without Ready");
                Set(hostView, "rotationMode", CarouselRotationMode.ComicAndFrame);
                Set(clientView, "rotationMode", CarouselRotationMode.ComicAndFrame);
                hostSession.StartGame();
                Check(hostView.FrameRectTransform.localScale != Vector3.one &&
                    Get<CutsceneCarousel.ComicPanel[]>(hostView, "comics")[2].image.rectTransform.localScale == Vector3.one * 0.8f,
                    "Replay resets selection scales to authored baselines");
                await CheckManualPages(roles, remote, hostView, clientView);
                roles.SelectRole(LobbyRole.Traveler);
                remote.SelectRole(LobbyRole.Hacker);
                await Confirm(remote);
                await Until(() => roles.Current.CountdownActive, "Second countdown started");
                generation = roles.Current.CountdownGeneration;
                client.Shutdown();
                await Until(() => !roles.Current.Client.Connected, "Client disconnect received");
                Check(roles.Current.Stage == SessionStage.Lobby && !roles.Current.CountdownActive &&
                    !roles.TryLockForStart(generation), "Disconnect cancels countdown and returns survivor to Lobby");
                await Task.Delay(300);
                Check(client.StartClient(), "Client reconnect started");
                await Until(() => clientSession.RoleSelection != null && roles.Current.Client.Connected, "Client reconnect receives new unassigned slot");
                Check(hostSession.CanStartGame && roles.Current.Client.Role == LobbyRole.Unassigned, "Rejoined pair can replay introduction");
                Debug.Log($"CUTSCENE LOOPBACK CHECKS PASSED: {checks} checks over real NGO/Unity Transport peers. UI screenshots: {evidenceFolder}");
            }
            catch (Exception error) { Debug.LogError($"CUTSCENE LOOPBACK CHECKS FAILED after {checks} checks: {error}"); }
            finally
            {
                if (client != null) client.Shutdown();
                hostSession.LeaveRoom();
                await Task.Delay(300);
                if (game != null) UnityEngine.Object.Destroy(game.gameObject);
                if (client != null) UnityEngine.Object.Destroy(client.gameObject);
                host.NetworkConfig.EnableSceneManagement = originalScenes;
                host.NetworkConfig.ForceSamePrefabs = originalSame;
                protocolField.SetValue(transport, originalProtocol);
                running = false;
            }
        }

        private static void CheckLobbyBackgrounds(NetworkSessionManager host, NetworkSessionManager client)
        {
            // Reuse the authored UI only for this fixture; production peers each own their own Canvas.
            var ui = host.GetComponent<LobbyUI>();
            var image = Get<Image>(ui, "lobbyPanelImage");
            var original = Get<Sprite>(ui, "playerOneLobbyBackground");
            var second = Get<Sprite>(ui, "playerTwoLobbyBackground");
            Debug.Log($"LOBBY BACKGROUND DIAGNOSTIC: image={image.name}, scene={image.gameObject.scene.name}, " +
                $"P1={original?.name}, P2={second?.name}, hostState={host.State}, clientState={client.State}, clientIsHost={client.IsHost}");
            Check(second != null && second.name == "Lobby Main Interface_Player2_0", "Exact P2 sprite resolves in Editor");
            try
            {
                Set(ui, "session", client);
                Call(client, "SetState", ConnectionState.Connected, "Connected fixture");
                Call(ui, "RefreshAll");
                Check(image.sprite == second && image.overrideSprite == second, "Connected Client displays P2 on actual passport Image");
                image.overrideSprite = original;
                Call(ui, "RefreshVisibleLobbyBackground");
                Check(image.overrideSprite == second, "Visible Lobby refresh clears stale sprite overrides");
                image.gameObject.SetActive(false);
                image.sprite = original;
                image.gameObject.SetActive(true);
                Call(ui, "RefreshVisibleLobbyBackground");
                Check(image.sprite == second, "Reactivating Lobby reapplies Client identity");
                Call(client, "SetState", ConnectionState.Disconnected, "Disconnected fixture");
                Call(ui, "RefreshAll");
                Check(image.sprite == original, "Client leaving resets background");
                Call(client, "SetState", ConnectionState.Connected, "Connected fixture");
                Call(ui, "RefreshAll");
                Check(image.sprite == second, "Client reconnect reapplies P2 background");
            }
            finally { Set(ui, "session", host); Call(ui, "RefreshAll"); }
            Check(image.sprite == original && image.overrideSprite == original, "Host retains P1 after role swap");
        }

        private static void CheckCarouselModes(CutsceneCarousel view)
        {
            var panels = Get<CutsceneCarousel.ComicPanel[]>(view, "comics");
            Vector2 center = view.FrameRectTransform.anchoredPosition;
            for (int index = 1; index <= 2; index++)
            {
                Set(view, "outgoingComic", index - 1);
                var state = new LobbyRoleState { ComicIndex = index, Stage = SessionStage.ComicEntering };
                foreach (float progress in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                {
                    Call(view, "AnimateComicOnlyRotation", state, true, progress);
                    Check(view.FrameRectTransform.anchoredPosition == center && view.FrameRectTransform.localScale == Vector3.one * 0.8f,
                        "ComicOnly keeps frame stationary at authored scale");
                    Call(view, "AnimateComicAndFrameRotation", state, true, progress);
                    for (int i = index - 1; i <= index; i++)
                    {
                        var frame = panels[i].rotatingFrame.rectTransform;
                        var comic = panels[i].image.rectTransform;
                        Check(frame.localScale == comic.localScale && frame.localRotation == comic.localRotation &&
                            frame.GetSiblingIndex() + 1 == comic.GetSiblingIndex(), "Paired frame follows depth/rotation directly behind comic");
                    }
                }
            }
            Call(view, "AnimateComicOnlyRotation", new LobbyRoleState { ComicIndex = 0 }, false, 1f);
        }

        private static async Task CheckLobbyArtwork(NetworkSessionManager session)
        {
            foreach (string name in new[] { "CopyCodeButton", "StartGameButton", "LeaveRoomButton" })
            {
                Button button = null;
                foreach (var candidate in session.GetComponentsInChildren<Button>(true))
                    if (candidate.name == name) { button = candidate; break; }
                Check(button != null, name + " exists in custom Lobby");
                var image = button.GetComponent<Image>();
                Sprite original = image.sprite;
                Check(original != null && button.targetGraphic == image && button.transition == Selectable.Transition.ColorTint,
                    name + " tints its original artwork Image");
                bool interactable = button.interactable;
                // Loopback has no Relay code, so LobbyUI legitimately hides Copy Code.
                bool active = button.gameObject.activeSelf;
                button.gameObject.SetActive(true);
                button.interactable = true;
                var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                await Task.Delay(150);
                Check(Mathf.Abs(image.canvasRenderer.GetColor().r - 0.88f) < 0.02f && image.overrideSprite == original,
                    name + $" hover preserves sprite and reaches 0.88 tint (actual {image.canvasRenderer.GetColor()}, active {image.gameObject.activeInHierarchy})");
                ScreenCapture.CaptureScreenshot(Path.Combine(evidenceFolder, name + "-hover.png"));
                await Task.Delay(100);
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                await Task.Delay(150);
                Check(Mathf.Abs(image.canvasRenderer.GetColor().r - 0.74f) < 0.02f && image.overrideSprite == original,
                    name + " pressed artwork remains visible");
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                EventSystem.current.SetSelectedGameObject(null);
                await Task.Delay(150);
                Check(Mathf.Abs(image.canvasRenderer.GetColor().r - 1) < 0.02f && image.overrideSprite == original,
                    name + " normal artwork restored");
                button.interactable = false;
                await Task.Delay(150);
                Check(image.canvasRenderer.GetColor().a == 1 && image.canvasRenderer.GetColor().r > 0.7f && image.overrideSprite == original,
                    name + " disabled artwork retains text and opacity");
                button.interactable = interactable;
                button.gameObject.SetActive(active);
            }
        }

        private static void CheckLocalHover(CutsceneCarousel host, CutsceneCarousel client, LobbyRoleSelection roles)
        {
            var person = Get<Button>(host, "personButton");
            var briefcase = Get<Button>(host, "briefcaseButton");
            var personHover = person.GetComponent<CharacterHoverFeedback>();
            var briefcaseHover = briefcase.GetComponent<CharacterHoverFeedback>();
            var personDescription = Get<GameObject>(personHover, "descriptionImage");
            var briefcaseDescription = Get<GameObject>(briefcaseHover, "descriptionImage");
            var pointer = new PointerEventData(EventSystem.current);
            var before = roles.Current;
            Material personOriginal = person.GetComponent<Image>().material;
            Material briefcaseOriginal = briefcase.GetComponent<Image>().material;
            Sprite personSprite = person.GetComponent<Image>().sprite;
            Sprite briefcaseSprite = briefcase.GetComponent<Image>().sprite;
            Color comicColor = Get<CutsceneCarousel.ComicPanel[]>(host, "comics")[2].image.color;
            Check(!personDescription.activeSelf && !briefcaseDescription.activeSelf, "Descriptions initially hidden");
            Check(!personDescription.GetComponent<Image>().raycastTarget && !briefcaseDescription.GetComponent<Image>().raycastTarget,
                "Description artwork cannot steal character pointer events");
            Check(Get<Image>(host, "personMarker").GetComponent<Text>() == null &&
                !Get<Image>(host, "personMarker").gameObject.activeSelf && !Get<Image>(host, "briefcaseMarker").gameObject.activeSelf,
                "Unselected characters have no marker Text or visible marker");
            foreach (var text in host.GetComponentsInChildren<Text>(true))
                Check(!string.Equals(text.text, "Available", StringComparison.OrdinalIgnoreCase), "No Available text in character prefab");
            ExecuteEvents.Execute(person.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            Check(personHover.IsHovered && person.GetComponent<Image>().material == Get<Material>(personHover, "hoverMaterial") && personDescription.activeSelf &&
                !briefcaseHover.IsHovered && !briefcaseDescription.activeSelf, "Person hover highlights only Person and its description");
            Check(!Get<Button>(client, "personButton").GetComponent<CharacterHoverFeedback>().IsHovered,
                "Person hover is not replicated to the other peer");
            ExecuteEvents.Execute(briefcase.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            Check(!personHover.IsHovered && person.GetComponent<Image>().material == personOriginal && !personDescription.activeSelf &&
                briefcaseHover.IsHovered && briefcaseDescription.activeSelf &&
                briefcase.GetComponent<Image>().material == Get<Material>(briefcaseHover, "hoverMaterial"), "Briefcase hover clears Person even before its exit event");
            ExecuteEvents.Execute(briefcase.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            Check(!briefcaseHover.IsHovered && briefcase.GetComponent<Image>().material == briefcaseOriginal && !briefcaseDescription.activeSelf,
                "Pointer exit restores the original material and hides the description");
            Check(person.GetComponent<Image>().sprite == personSprite && briefcase.GetComponent<Image>().sprite == briefcaseSprite,
                "Character hover never replaces either sprite");
            ExecuteEvents.Execute(person.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            Get<GameObject>(host, "characterSelection").SetActive(false);
            Get<GameObject>(host, "characterSelection").SetActive(true);
            Check(!personHover.IsHovered && !personDescription.activeSelf, "Panel disable/re-enable clears stale hover");
            Check(roles.Current.Equals(before) && Get<CutsceneCarousel.ComicPanel[]>(host, "comics")[2].image.color == comicColor,
                "Hover changes neither network selection nor the comic image");
        }

        private static async Task CheckClickOwnership(CutsceneCarousel host, CutsceneCarousel client,
            LobbyRoleSelection roles, LobbyRoleSelection remote)
        {
            var hostPerson = Get<Button>(host, "personButton");
            var clientPerson = Get<Button>(client, "personButton");
            hostPerson.onClick.Invoke();
            await Confirm(roles);
            await Until(() => remote.Current.Host.Role == LobbyRole.Traveler, "P1 click claims Person on both peers");
            Check(Get<Image>(client, "personMarker").gameObject.activeInHierarchy &&
                Get<Image>(client, "personMarker").sprite == Get<Sprite>(client, "playerOneMarkerSprite"), "P1 artwork displayed remotely on Person");
            var pointer = new PointerEventData(EventSystem.current);
            var clientCanvas = client.GetComponent<Canvas>();
            clientCanvas.enabled = false; // Isolate the Host view for screenshots in this same-process fixture.
            try
            {
                foreach (string field in new[] { "personButton", "briefcaseButton" })
                {
                    var hovered = Get<Button>(host, field);
                    ExecuteEvents.Execute(hovered.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                    ScreenCapture.CaptureScreenshot(Path.Combine(evidenceFolder, field + "-hover.png"));
                    await Task.Delay(150);
                    ExecuteEvents.Execute(hovered.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                }
            }
            finally { clientCanvas.enabled = true; }
            var before = roles.Current;
            clientPerson.onClick.Invoke();
            Check(roles.Current.Equals(before) && !remote.HasPendingRequest, "P2 click on occupied Person does nothing");
            hostPerson.onClick.Invoke();
            await Confirm(roles);
            await Until(() => remote.Current.Host.Role == LobbyRole.Unassigned, "P1 second click deselects Person");
            Check(!Get<Image>(host, "personMarker").gameObject.activeSelf &&
                !Get<Image>(client, "personMarker").gameObject.activeSelf, "Deselection hides marker on both peers");
            clientPerson.onClick.Invoke();
            await Confirm(remote);
            await Until(() => roles.Current.Client.Role == LobbyRole.Traveler, "P2 can claim Person released by P1");
            Check(Get<Image>(host, "personMarker").sprite == Get<Sprite>(host, "playerTwoMarkerSprite"), "P2 artwork replaces P1 marker only after accepted claim");
            clientPerson.onClick.Invoke();
            await Confirm(remote);
            await Until(() => roles.Current.Client.Role == LobbyRole.Unassigned, "P2 second click releases Person");
            hostPerson.onClick.Invoke();
            await Confirm(roles);
            Get<Button>(host, "briefcaseButton").onClick.Invoke();
            await Confirm(roles);
            Check(roles.Current.Host.Role == LobbyRole.Hacker && !Get<Image>(host, "personMarker").gameObject.activeSelf,
                "Clicking free Briefcase switches P1 directly and clears Person marker");
            Get<Button>(host, "briefcaseButton").onClick.Invoke();
            await Confirm(roles);
            await Until(() => remote.Current.Host.Role == LobbyRole.Unassigned, "Clicking own Briefcase releases it");
        }

        private static async Task CheckManualPages(LobbyRoleSelection host, LobbyRoleSelection client,
            CutsceneCarousel hostView, CutsceneCarousel clientView)
        {
            await Until(() => client.Current.Stage == SessionStage.ComicNarration, "Manual introduction starts");
            await Task.Delay(500); // Longer than the old fixture's narration timer.
            Check(host.Current.Host.CutscenePage == 0 && host.Current.Client.CutscenePage == 0,
                "Pages never advance automatically");
            host.NavigateCutscene(-1);
            Check(host.Current.Host.CutscenePage == 0, "Left arrow on first page stays in bounds");
            var panels = Get<CutsceneCarousel.ComicPanel[]>(hostView, "comics");
            Set(hostView, "transitionDuration", .6f);
            host.NavigateCutscene(1);
            await Task.Delay(180);
            Check(panels[0].image.gameObject.activeSelf && panels[1].image.gameObject.activeSelf &&
                panels[1].image.rectTransform.localRotation != Quaternion.identity &&
                panels[1].image.rectTransform.localRotation == panels[1].rotatingFrame.rectTransform.localRotation,
                "Manual right arrow rotates outgoing and incoming comic/frame pairs");
            await Task.Delay(650);
            host.NavigateCutscene(-1);
            await Task.Delay(180);
            Check(panels[0].image.gameObject.activeSelf && panels[1].image.gameObject.activeSelf &&
                panels[0].image.rectTransform.anchoredPosition.x < Get<Vector2[]>(hostView, "comicCenters")[0].x,
                "Left arrow reverses the rotation");
            await Task.Delay(650);
            var clientCanvas = clientView.GetComponent<Canvas>();
            clientCanvas.enabled = false;
            ScreenCapture.CaptureScreenshot(Path.Combine(evidenceFolder, "reading-layout.png"));
            await Task.Delay(150);
            clientCanvas.enabled = true;
            Set(hostView, "transitionDuration", .1f);
            for (int page = 1; page <= 3; page++)
            {
                host.NavigateCutscene(1);
                await Until(() => client.Current.Host.CutscenePage == page, "Host page replicated");
                Check(client.Current.Client.CutscenePage == 0, "Host navigation never advances client");
            }
            await Until(() => Get<GameObject>(hostView, "characterSelection").activeSelf, "Early player sees selection waiting screen");
            Check(!Get<Button>(hostView, "personButton").interactable &&
                Get<Text>(hostView, "navigationHint").text.Contains("WAITING"), "Selection waits for other reader");
            host.SelectRole(LobbyRole.Traveler);
            await Confirm(host);
            Check(host.Current.Host.Role == LobbyRole.Unassigned, "Server rejects selection before both arrive");
            host.NavigateCutscene(-1);
            await Until(() => client.Current.Host.CutscenePage == 2, "Waiting player can go back");
            Check(Get<Text>(hostView, "narrationText").text == Get<CutsceneCarousel.ComicPanel[]>(hostView, "comics")[2].narration[0].text,
                "Back restores third comic narration");
            host.NavigateCutscene(1);
            for (int page = 1; page <= 3; page++)
            {
                client.NavigateCutscene(1);
                await Until(() => client.Current.Client.CutscenePage == page, "Client page confirmed");
                if (page < 3)
                {
                    await Task.Delay((int)(clientView.TransitionDuration * 1000) + 100);
                    var text = Get<Text>(clientView, "narrationText");
                    Check(text.text == Get<CutsceneCarousel.ComicPanel[]>(clientView, "comics")[page].narration[0].text,
                        "Client sees own page's complete narration");
                    Check(text.preferredHeight <= text.rectTransform.rect.height, "Manual page narration fits");
                }
            }
            await Until(() => client.Current.Stage == SessionStage.CharacterSelection &&
                Get<Button>(clientView, "personButton").interactable &&
                Get<Button>(hostView, "personButton").interactable, "Both arrive and selection zoom finishes");
            Check(!Get<Text>(hostView, "navigationHint").gameObject.activeSelf, "Navigation prompt hidden during shared selection");
            client.NavigateCutscene(-1);
            await Task.Delay(50);
            Check(client.Current.Client.CutscenePage == 3, "Shared selection cannot be left during role assignment");
        }

        private static async Task CheckEndScreensAndRestart(CoopGameNetworkBridge host, CoopGameNetworkBridge client)
        {
            var first = host.GetComponentInChildren<InterviewController>(true);
            var second = client.GetComponentInChildren<InterviewController>(true);
            Set(first, "useOllama", false);
            Set(second, "useOllama", false);
            Set(host, "ready", true);
            host.StartNewRound();
            await Until(() => client.CurrentState.Round == host.CurrentState.Round && client.CurrentState.Round > 0,
                "Initial preparation round synchronized");
            uint round = host.CurrentState.Round;
            Get<Button>(second, "restartButton").onClick.Invoke();
            await Task.Delay(100);
            Check(host.CurrentState.Round == round, "Restart blocked during active game");
            const string submittedAnswer = "I AM HERE FOR CULTURAL EXCHANGE.";
            typeof(InterviewController).GetProperty("LastSubmittedAnswer").SetValue(first, submittedAnswer);
            typeof(InterviewController).GetProperty("ReviewingPurpose").SetValue(first, true);
            Call(host, "Publish", host.CurrentState);
            await Until(() => second.ReviewingPurpose && second.LastSubmittedAnswer == submittedAnswer,
                "Submitted traveler answer reaches the other peer during review");
            foreach (var bridge in new[] { host, client })
            {
                var hackerView = bridge.GetComponentInChildren<HackerInterviewView>(true);
                Call(hackerView, "RefreshQuestion");
                Check(Get<TMPro.TMP_Text>(hackerView, "currentQuestionText").text == "Your partner answered:\n" + submittedAnswer,
                    "Hacker review subtitle displays the submitted answer");
            }
            typeof(InterviewController).GetProperty("ReviewingPurpose").SetValue(first, false);
            Call(host, "Publish", host.CurrentState);
            await Until(() => !second.ReviewingPurpose, "Review completion replicated");
            var returnedView = client.GetComponentInChildren<HackerInterviewView>(true);
            Call(returnedView, "RefreshQuestion");
            Check(!Get<TMPro.TMP_Text>(returnedView, "currentQuestionText").text.StartsWith("Your partner answered:"),
                "Answer subtitle clears after review");
            foreach (bool approved in new[] { false })
            {
                Call(first, "Finish", approved, "Ending check");
                Call(host, "Publish", host.CurrentState);
                await Until(() => second.IsFinished, "Ending synchronized to both players");
                foreach (var view in new[] { first, second })
                {
                    Check(Get<Image>(view, "resultImage").sprite == Get<Sprite>(view, approved ? "successSprite" : "failureSprite"),
                        "Correct ending artwork displayed");
                    Check(Get<GameObject>(view, "gameOverPanel").activeInHierarchy && Get<Button>(view, "restartButton").gameObject.activeInHierarchy,
                        "Ending and restart button visible");
                    Check(!Get<TMPro.TMP_Text>(view, "resultText").gameObject.activeSelf &&
                        !Get<TMPro.TMP_Text>(view, "reasonText").gameObject.activeSelf, "Old text does not cover artwork");
                }
                ScreenCapture.CaptureScreenshot(Path.Combine(evidenceFolder, approved ? "ending-success.png" : "ending-failure.png"));
                await Task.Delay(100);
                // The scene-managed Restart now unloads this presentation.
                // Its role-selection destination is checked by the caller.

            }
        }

        private static async Task Confirm(LobbyRoleSelection roles)
        {
            await Until(() => !roles.HasPendingRequest, "Request confirmed by Host");
            await Task.Delay(40);
        }
        private static async Task Until(Func<bool> condition, string label)
        {
            double deadline = EditorApplication.timeSinceStartup + 12;
            while (!condition())
            {
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(label);
                await Task.Delay(20);
            }
            Check(true, label);
        }
        private static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks++;
        }
        private static void Call(object target, string method, params object[] arguments) => target.GetType().GetMethod(method, Private).Invoke(target, arguments);
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    }
}
#endif
