using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.Multiplayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkSessionManager))]
    public sealed class LobbyUI : MonoBehaviour
    {
        [SerializeField] private GameObject multiplayerCanvasPrefab;

        private NetworkSessionManager session;
        private GameObject multiplayerCanvasInstance;
        private GameObject mainMenuPanel;
        private GameObject lobbyPanel;
        private InputField joinCodeInput;
        private Button createButton;
        private Button joinButton;
        private Button copyButton;
        private Button leaveButton;
        private Button exitBackButton;
        private Text menuStatusText;
        private Text joinCodeText;
        private Text playerCountText;
        private Text playerStatusText;
        private bool initialized;

        private void Awake()
        {
            session = GetComponent<NetworkSessionManager>();
            initialized = InitializeInterface();
            if (!initialized)
            {
                enabled = false;
                return;
            }

            EnsureEventSystem();
            BindButtonListeners();
        }

        private void OnEnable()
        {
            if (!initialized)
                return;

            session.StateChanged += OnStateChanged;
            session.PlayersChanged += OnPlayersChanged;
            session.JoinCodeChanged += OnJoinCodeChanged;
            RefreshAll();
        }

        private void OnDisable()
        {
            if (!initialized || session == null)
                return;

            session.StateChanged -= OnStateChanged;
            session.PlayersChanged -= OnPlayersChanged;
            session.JoinCodeChanged -= OnJoinCodeChanged;
        }

        private void OnDestroy()
        {
            if (initialized)
                UnbindButtonListeners();
        }

        private bool InitializeInterface()
        {
            if (multiplayerCanvasPrefab == null)
            {
                Debug.LogError($"{nameof(LobbyUI)} on '{name}' requires a MultiplayerCanvas prefab reference.", this);
                return false;
            }

            multiplayerCanvasInstance = FindExistingCanvas();
            if (multiplayerCanvasInstance == null)
            {
                multiplayerCanvasInstance = Instantiate(multiplayerCanvasPrefab, transform, false);
                multiplayerCanvasInstance.name = multiplayerCanvasPrefab.name;
            }

            Transform root = multiplayerCanvasInstance.transform;
            bool valid = true;
            valid &= TryBind(root, "MainMenuPanel", out mainMenuPanel);
            valid &= TryBind(root, "LobbyPanel", out lobbyPanel);
            valid &= TryBind(root, "JoinCodeInput", out joinCodeInput);
            valid &= TryBind(root, "CreateRoomButton", out createButton);
            valid &= TryBind(root, "JoinRoomButton", out joinButton);
            valid &= TryBind(root, "CopyCodeButton", out copyButton);
            valid &= TryBind(root, "LeaveRoomButton", out leaveButton);
            valid &= TryBind(root, "ExitBackButton", out exitBackButton);
            valid &= TryBind(root, "StatusText", out menuStatusText);
            valid &= TryBind(root, "JoinCodeText", out joinCodeText);
            valid &= TryBind(root, "PlayerCountText", out playerCountText);
            valid &= TryBind(root, "PlayerStatusText", out playerStatusText);

            if (!valid)
                Debug.LogError($"{nameof(LobbyUI)} could not bind the required controls in " +
                    $"'{multiplayerCanvasPrefab.name}'. The prefab hierarchy was not modified.", this);

            return valid;
        }

        private GameObject FindExistingCanvas()
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            GameObject match = null;
            int matchCount = 0;
            foreach (Canvas canvas in canvases)
            {
                if (!string.Equals(canvas.gameObject.name, multiplayerCanvasPrefab.name, StringComparison.Ordinal))
                    continue;

                match ??= canvas.gameObject;
                matchCount++;
            }

            if (matchCount > 1)
                Debug.LogError($"Found {matchCount} scene instances named '{multiplayerCanvasPrefab.name}'. " +
                    $"{nameof(LobbyUI)} will reuse the first instance and will not create another one.", this);

            return match;
        }

        private bool TryBind<T>(Transform root, string objectName, out T component) where T : Component
        {
            component = null;
            if (!TryFindUniqueChild(root, objectName, out Transform match))
                return false;

            component = match.GetComponent<T>();
            if (component != null)
                return true;

            Debug.LogError($"'{objectName}' under '{root.name}' is missing the required {typeof(T).Name} component.",
                this);
            return false;
        }

        private bool TryBind(Transform root, string objectName, out GameObject target)
        {
            target = null;
            if (!TryFindUniqueChild(root, objectName, out Transform match))
                return false;

            target = match.gameObject;
            return true;
        }

        private bool TryFindUniqueChild(Transform root, string objectName, out Transform match)
        {
            match = null;
            int matchCount = 0;
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform descendant in descendants)
            {
                if (!string.Equals(descendant.name, objectName, StringComparison.Ordinal))
                    continue;

                match ??= descendant;
                matchCount++;
            }

            if (matchCount == 1)
                return true;

            Debug.LogError($"Expected exactly one '{objectName}' under '{root.name}', found {matchCount}.", this);
            return false;
        }

        private void BindButtonListeners()
        {
            createButton.onClick.RemoveListener(CreateRoom);
            createButton.onClick.AddListener(CreateRoom);
            joinButton.onClick.RemoveListener(JoinRoom);
            joinButton.onClick.AddListener(JoinRoom);
            copyButton.onClick.RemoveListener(CopyJoinCode);
            copyButton.onClick.AddListener(CopyJoinCode);
            leaveButton.onClick.RemoveListener(LeaveRoom);
            leaveButton.onClick.AddListener(LeaveRoom);
            exitBackButton.onClick.RemoveListener(LeaveRoom);
            exitBackButton.onClick.AddListener(LeaveRoom);
        }

        private void UnbindButtonListeners()
        {
            createButton.onClick.RemoveListener(CreateRoom);
            joinButton.onClick.RemoveListener(JoinRoom);
            copyButton.onClick.RemoveListener(CopyJoinCode);
            leaveButton.onClick.RemoveListener(LeaveRoom);
            exitBackButton.onClick.RemoveListener(LeaveRoom);
        }

        private void CreateRoom()
        {
            session.CreateRoom();
        }

        private void JoinRoom()
        {
            session.JoinRoom(joinCodeInput.text);
        }

        private void LeaveRoom()
        {
            session.LeaveRoom();
        }

        private void RefreshAll()
        {
            OnStateChanged(session.State, session.StatusMessage);
            OnPlayersChanged(session.Players);
            OnJoinCodeChanged(session.JoinCode);
        }

        private void OnStateChanged(ConnectionState state, string message)
        {
            bool showLobby = state == ConnectionState.Connected;
            mainMenuPanel.SetActive(!showLobby);
            lobbyPanel.SetActive(showLobby);
            menuStatusText.text = message;
            createButton.interactable = !session.IsBusy;
            joinButton.interactable = !session.IsBusy;
            joinCodeInput.interactable = !session.IsBusy;
        }

        private void OnPlayersChanged(IReadOnlyList<NetworkPlayerInfo> players)
        {
            playerCountText.text = $"Players: {players.Count} / {NetworkSessionManager.MaximumPlayers}";
            StringBuilder status = new();
            for (int index = 0; index < NetworkSessionManager.MaximumPlayers; index++)
            {
                if (index < players.Count)
                {
                    NetworkPlayerInfo player = players[index];
                    string role = player.IsHost ? "Host" : "Client";
                    status.AppendLine($"Player {index + 1}: Connected  (Client ID {player.ClientId}, {role})");
                }
                else
                {
                    status.AppendLine($"Player {index + 1}: Waiting...");
                }
            }

            if (players.Count < NetworkSessionManager.MaximumPlayers && session.IsHost)
                status.Append("\nWaiting for Player 2...");
            playerStatusText.text = status.ToString();
        }

        private void OnJoinCodeChanged(string code)
        {
            joinCodeText.text = string.IsNullOrEmpty(code) ? "Join Code: unavailable" : $"Join Code: {code}";
            copyButton.gameObject.SetActive(session.IsHost && !string.IsNullOrEmpty(code));
        }

        private void CopyJoinCode()
        {
            if (!string.IsNullOrEmpty(session.JoinCode))
                GUIUtility.systemCopyBuffer = session.JoinCode;
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
                return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
