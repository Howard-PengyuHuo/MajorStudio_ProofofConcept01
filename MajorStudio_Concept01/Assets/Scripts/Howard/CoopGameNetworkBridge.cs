using System;
using System.Collections.Generic;
using CarryOn.Taylor;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Game.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CoopGameNetworkBridge : NetworkBehaviour
    {
        [SerializeField] private GameObject gameplayCanvasPrefab;
        private readonly NetworkVariable<CoopGameSnapshot> snapshot = new(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly Dictionary<ulong, ulong> lastRequests = new();
        private GameObject presentation;
        private PreparationSession preparation;
        private PreparationPreview preview;
        private InterviewController interview;
        private ulong nextRequest;
        private uint appliedRound, appliedRevision, notifiedShake;
        private float nextDisplayRefresh;
        private bool ready, subscribed;
        private enum Request : byte { Start, Edit, Print, Answer, Shake }

        public CoopGameSnapshot CurrentState => snapshot.Value;
        public PlayerRole LocalRole => NetworkManager.LocalClientId == snapshot.Value.TravelerId
            ? PlayerRole.Traveler : PlayerRole.Hacker;
        public event Action HelpRequested;

        private void Awake()
        {
            if (gameplayCanvasPrefab == null)
            {
                Debug.LogError("CoopGameNetworkBridge requires the existing gameplay Canvas prefab.", this);
                return;
            }

            // Keep all prefab behaviours inactive until network mode and role are configured.
            presentation = new GameObject("GameplayPresentation");
            presentation.transform.SetParent(transform, false);
            presentation.SetActive(false);
            GameObject canvas = Instantiate(gameplayCanvasPrefab, presentation.transform, false);
            preparation = canvas.GetComponentInChildren<PreparationSession>(true);
            preview = canvas.GetComponentInChildren<PreparationPreview>(true);
            interview = canvas.GetComponentInChildren<InterviewController>(true);
            if (preparation == null || preview == null || interview == null)
            {
                Debug.LogError("Gameplay Canvas must contain PreparationSession, PreparationPreview and InterviewController.", this);
                return;
            }
            preparation.EnterNetworkMode();
            interview.EnterNetworkMode(false);
        }

        public override void OnNetworkSpawn()
        {
            if (preparation == null || preview == null || interview == null) return;
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            snapshot.OnValueChanged += OnSnapshotChanged;
            preparation.StartRequested += RequestStart;
            preparation.IdentityEditRequested += RequestEdit;
            preparation.PrintRequested += RequestPrint;
            interview.AnswerRequested += RequestAnswer;
            interview.ShakeRequested += RequestShake;
            if (NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
            subscribed = true;
            if (IsServer)
            {
                ulong hacker = ulong.MaxValue;
                foreach (ulong id in NetworkManager.ConnectedClientsIds)
                    if (id != Unity.Netcode.NetworkManager.ServerClientId) hacker = id;
                snapshot.Value = new CoopGameSnapshot
                {
                    TravelerId = Unity.Netcode.NetworkManager.ServerClientId, HackerId = hacker,
                    Revision = 1, Phase = EntryPhase.Waiting, ResultReason = string.Empty
                };
            }
            ApplySnapshot(snapshot.Value);
        }

        private void OnSceneLoaded(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (!IsServer || sceneName != gameObject.scene.name || ready) return;
            if (timedOut.Count != 0 || completed.Count != NetworkSessionManager.MaximumPlayers)
            {
                Debug.LogWarning("Preparation was not started because both players did not finish loading GameScene.", this);
                return;
            }
            ready = true;
            StartNewRound();
        }

        // Host-only entry point for a subsequent round; active rounds cannot restart accidentally.
        [ContextMenu("Start New Round (Host)")]
        public void StartNewRound()
        {
            if (!IsSpawned || !IsServer || !ready ||
                NetworkManager.ConnectedClientsIds.Count != NetworkSessionManager.MaximumPlayers ||
                (snapshot.Value.Round != 0 && !snapshot.Value.IsFinished)) return;
            CoopGameSnapshot state = snapshot.Value;
            state.Round++;
            state.ShakeSequence = 0;
            state.PreparationEndsAt = NetworkManager.ServerTime.Time + preparation.PreparationSeconds;
            lastRequests.Clear();
            preparation.BeginPreparation(preparation.PreparationSeconds);
            Publish(state);
        }

        private void Update()
        {
            if (!IsSpawned || preparation == null || snapshot.Value.Revision == 0) return;
            ExpirePreparation();
            if (snapshot.Value.Phase != EntryPhase.Preparation || Time.unscaledTime < nextDisplayRefresh) return;
            nextDisplayRefresh = Time.unscaledTime + 0.1f;
            ApplyPreparation(snapshot.Value);
        }

        private void ExpirePreparation()
        {
            CoopGameSnapshot state = snapshot.Value;
            if (!IsServer || state.Phase != EntryPhase.Preparation ||
                NetworkManager.ServerTime.Time < state.PreparationEndsAt) return;
            preparation.ApplyNetworkSnapshot(EntryPhase.Interview, 0, preparation.Identity,
                preparation.HasPrintedIdentity, preparation.PrintedIdentity);
            Publish(state);
        }

        private void Publish(CoopGameSnapshot state)
        {
            state.Revision++;
            state.Phase = preparation.Phase;
            state.Identity = new NetworkIdentity(preparation.Identity);
            state.PrintedIdentity = new NetworkIdentity(preparation.PrintedIdentity);
            state.HasPrintedIdentity = preparation.HasPrintedIdentity;
            state.QuestionIndex = interview.QuestionIndex;
            state.Reputation = interview.Reputation;
            state.IsFinished = interview.IsFinished;
            state.EntryApproved = interview.EntryApproved;
            state.ResultReason = interview.ResultReason;
            snapshot.Value = state;
        }

        private void OnSnapshotChanged(CoopGameSnapshot previous, CoopGameSnapshot current) => ApplySnapshot(current);

        private void ApplySnapshot(CoopGameSnapshot state)
        {
            if (state.Revision == 0 || state.Revision <= appliedRevision || preparation == null) return;
            appliedRevision = state.Revision;
            bool traveler = NetworkManager.LocalClientId == state.TravelerId;
            interview.EnterNetworkMode(traveler, IsServer);
            if (state.Round != appliedRound)
            {
                appliedRound = state.Round;
                notifiedShake = 0;
                // Waiting forces a reset even if the previous round finished in Interview.
                preparation.ApplyNetworkSnapshot(EntryPhase.Waiting, 0, default, false, default);
            }
            preview.SetLocalRole(traveler ? PlayerRole.Traveler : PlayerRole.Hacker);
            if (!presentation.activeSelf) presentation.SetActive(true);
            ApplyPreparation(state);
            interview.ApplyNetworkSnapshot(state.QuestionIndex, state.Reputation,
                state.IsFinished, state.EntryApproved, state.ResultReason);
            while (notifiedShake < state.ShakeSequence)
            {
                notifiedShake++;
                interview.NotifyNetworkShake();
                HelpRequested?.Invoke();
            }
        }

        private void ApplyPreparation(CoopGameSnapshot state)
        {
            float remaining = state.Phase == EntryPhase.Preparation
                ? (float)Math.Max(0, state.PreparationEndsAt - NetworkManager.ServerTime.Time) : 0;
            preparation.ApplyNetworkSnapshot(state.Phase, remaining, state.Identity.ToCard(),
                state.HasPrintedIdentity, state.PrintedIdentity.ToCard());
        }

        private void RequestStart() => SendRequest(Request.Start);
        private void RequestEdit(IdentityField field, string value) => SendRequest(Request.Edit, (int)field, value);
        private void RequestPrint() => SendRequest(Request.Print);
        private void RequestAnswer(int question, string value) => SendRequest(Request.Answer, question, value?.Trim());
        private void RequestShake() => SendRequest(Request.Shake);

        private void SendRequest(Request request, int argument = 0, string value = "")
        {
            if (!IsSpawned || snapshot.Value.Revision == 0) return;
            GameplayRequestRpc(snapshot.Value.Round, ++nextRequest, request, argument, NetworkIdentity.Text(value));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void GameplayRequestRpc(uint round, ulong requestId, Request request, int argument,
            FixedString512Bytes value, RpcParams rpcParams = default)
        {
            ProcessRequest(rpcParams.Receive.SenderClientId, round, requestId, request, argument, value.ToString());
        }

        private void ProcessRequest(ulong sender, uint round, ulong requestId, Request request, int argument, string value)
        {
            if (!IsServer || !ready || round != snapshot.Value.Round || requestId == 0 ||
                !NetworkManager.ConnectedClients.ContainsKey(sender)) return;
            if (lastRequests.TryGetValue(sender, out ulong last) && requestId <= last) return;
            lastRequests[sender] = requestId;
            ExpirePreparation(); // A request arriving after the deadline cannot slip in before Update.
            CoopGameSnapshot state = snapshot.Value;
            if (request == Request.Start)
            {
                if (sender == state.TravelerId) StartNewRound();
                return;
            }
            if (state.IsFinished) return;
            bool hacker = sender == state.HackerId;
            bool traveler = sender == state.TravelerId;
            if (request == Request.Edit && hacker && state.Phase == EntryPhase.Preparation &&
                !state.HasPrintedIdentity && Enum.IsDefined(typeof(IdentityField), argument))
                preparation.ApplyIdentityEdit((IdentityField)argument, value);
            else if (request == Request.Print && hacker && preparation.CanPrint)
                preparation.PrintIdentity();
            else if (request == Request.Answer && traveler && state.Phase == EntryPhase.Interview &&
                argument == interview.QuestionIndex && !string.IsNullOrWhiteSpace(value))
                interview.EvaluateAnswer(value);
            else if (request == Request.Shake && traveler && state.Phase == EntryPhase.Interview)
            {
                if (interview.ApplyShake()) state.ShakeSequence++;
            }
            else return;
            Publish(state);
        }

        public override void OnNetworkDespawn() => Unsubscribe();

        private void Unsubscribe()
        {
            if (subscribed)
            {
                snapshot.OnValueChanged -= OnSnapshotChanged;
                preparation.StartRequested -= RequestStart;
                preparation.IdentityEditRequested -= RequestEdit;
                preparation.PrintRequested -= RequestPrint;
                interview.AnswerRequested -= RequestAnswer;
                interview.ShakeRequested -= RequestShake;
                if (NetworkManager != null && NetworkManager.SceneManager != null)
                    NetworkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
                subscribed = false;
            }
            if (presentation != null) presentation.SetActive(false);
        }

        public override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
        }
    }
}
