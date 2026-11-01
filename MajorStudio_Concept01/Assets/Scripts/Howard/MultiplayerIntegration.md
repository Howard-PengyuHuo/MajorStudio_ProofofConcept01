# Cooperative multiplayer integration

Current stage (2026-10-29): see [RoleSelection.md](RoleSelection.md). Online gameplay now enters
role-based, non-interactive Waiting UI after role selection and Ready confirmation. The gameplay
synchronization described below and its older checks are historical hooks retained for a later
stage; Gameplay Synchronization Enabled is disabled on the CooperativeGameState prefab.

Open `Assets/Scenes/MultiplayerTest.unity`. Create a Relay room, join it from the second instance,
and click **START GAME** on the Host once the Lobby shows 2/2. NGO loads `GameScene` additively
on both peers. The lobby scene stays loaded, retaining its camera, EventSystem and persistent
NetworkManager. The lobby Canvas is hidden during gameplay. Preparation begins only after
NGO confirms that both players loaded the scene.

## Components and assets

- `CoopGameNetworkBridge` is on the scene-registered `CooperativeGameState` NetworkObject in
  `Assets/Scenes/GameScene.unity`. Its prefab is `Assets/Prefabs/CooperativeGameState.prefab`.
- The bridge's Gameplay Canvas Prefab reference points to the existing
  `Assets/Prefabs/Taylor/Canvas.prefab`. One local copy is instantiated under an initially
  inactive presentation parent. Before activation, the adapter disables local simulation and
  configures InterviewController for network requests. All prefab-internal references are retained.
- The existing gameplay Canvas prefab and Taylor's offline scene are unchanged. Offline auto-start,
  local timing and F1/F2 preview remain available there. Online local simulation is disabled, so the
  same preview component does not auto-start or process F1/F2.
- The lobby prefab has one added StartGameButton, copied from its existing button styling and
  controlled by LobbyUI. Existing lobby controls and their visual settings are retained.
- Both scenes are enabled in Build Settings. No new packages are required.
- `Tools > Cooperative Game > Configure Integration Assets` repairs these references and adds the
  Start button if absent. It is an explicit editor command, never an automatic import operation.
  It does not modify the gameplay Canvas asset. Normal use needs no manual Inspector assignment.

## Authority and replication

The Host assigns its client ID to Traveler and the joining client's ID to Hacker. The assignment
is part of the server-written `CoopGameSnapshot`; local UI selection is applied through the
existing `PreparationPreview.SetLocalRole` method before interaction.

One NetworkVariable contains the round/revision, roles, phase, server deadline, current identity,
printed identity, question index, reputation, result, reason, and help-event sequence. Identity
strings are bounded to the existing 64-character rule and serialized with fixed-capacity UTF-8
strings. Result reasons use explicit string serialization to retain the Host's Inspector text. Timer display is
refreshed locally at 10 Hz using NGO ServerTime; only the Host changes phase when the deadline
expires. No per-frame timer state is sent over the network.

RPCs include a round ID, monotonically increasing request ID, and (for answers) the question
index. The Host validates the actual RPC sender, assigned role, round, phase, print lock,
completion and current question. Repeated/old requests are ignored. Late edits are rejected
against the authoritative deadline even if they arrive before that frame's Update.

The phase and final identity/printed card are published atomically. PreparationSession applies
both identities before firing PreparationCompleted. InterviewController keeps its original
Inspector-configured questions, matching rules, reputation cost and result messages. Its
authority methods do not depend on local panel visibility. Replicated results update internal
finished state and show the existing GameOver panel; repeated result snapshots do not fire
Finished or entry-result UnityEvents again.

Accepted Shake requests update reputation on the Host only. Each peer consumes each replicated
help sequence once, invokes the existing `onShakeRequested` UnityEvent for local presentation,
and raises `CoopGameNetworkBridge.HelpRequested`. The Hacker receives the event; the current
prefab has no visual help listener configured. No new notification UI, screen shake or voice
feature is added. Network request forwarding uses a separate C# event, so the presentation
UnityEvent does not automatically send another request.

Draggable documents, transforms, sibling order, animation and focus remain local.

## Round and session lifecycle

`CoopGameNetworkBridge.StartNewRound()` is Host-only, accepts Waiting or a finished round,
and refuses to restart an active round. It resets identity, print state, deadline, question
progress, result, reputation and help sequence. No new restart button is added to the existing
gameplay UI; future UI can call this public entry point.

`NetworkSessionManager.ReturnToLobby()` lets the Host unload GameScene for both peers without
closing Relay. The Host can then start a new game with the same partner. `LeaveRoom()` retains
its existing session shutdown behavior and unloads any remaining local GameScene after NGO
shutdown. Mid-game joining is rejected; return to the Lobby before accepting a replacement
partner. Network subscriptions are released when the bridge despawns/destroys.

## Testing with Multiplayer Play Mode 2.0

1. Open MultiplayerTest as the only starting scene.
2. Open **Window > Play Mode > Scenarios** and select **Configure play mode scenarios**.
3. Configure the main Editor plus one additional Editor instance, both using the same scene/build.
4. Enter Play Mode. Create Room in the main instance; enter its code in the other instance.
5. With 2/2 in the Lobby, click START GAME on the Host.
6. Verify Traveler on Host and Hacker on Client. Edit all four fields and Print on the Hacker.
7. Wait for the configured preparation duration (currently 20 seconds in the existing prefab).
8. Submit printed Name, printed Nationality, and `believer` using Enter on Traveler.
9. In the Host Inspector, use CooperativeGameState's bridge component menu **Start New Round (Host)**
   to test an incorrect answer and three Shake
   requests (50 → 30 → 10 → 0). Verify the existing GameOver panel on both peers.
10. NetworkSessionManager's component menu offers **Return To Lobby (Host)** and **Leave Room**
    during Play Mode. The equivalent public methods can be connected to future UI.

For internet verification, build the same project on two computers on different networks and
follow the same Relay-code workflow. Local loopback or same-Mac Relay testing does not prove
connectivity between two physical networks.

## Repeatable automated checks

- In Play Mode in an idle MultiplayerTest Lobby, choose
  `Tools > Cooperative Game > Run Loopback Gameplay Checks (Play Mode)`. This uses two actual
  NGO/Unity Transport peers in one Editor, replicates the actual bridge prefab, and checks roles,
  identity/print, timer agreement, answers, request replay/role rejection, victory/failure,
  reputation/help, round reset, duplicate results and offline behavior. It supplies scene readiness
  in the fixture and does not test Relay or NGO scene loading. Stop Play Mode after the checks.
- On macOS in Edit Mode, choose `Build Relay Check Player (macOS)` in the same menu. It builds
  a temporary development player under the OS temporary directory. Enter Play Mode in the idle
  MultiplayerTest Lobby, then choose `Run Relay Scene Checks (Play Mode)`. This creates a real
  Relay Host and launches the separate player as Client, runs one round through the actual
  scene flow and real countdown, returns to Lobby and tests Host disconnection. It prints the
  exact client log path. This uses configured Unity Services and requires network access.
- The opt-in standalone check driver attaches only for `--coop-check-code=...` and is excluded
  from non-development builds. Normal player launches do not run test actions.

## Executed verification (2026-10-26)

- Unity 6000.5.4f1 completed script compilation with zero compiler errors/warnings.
- A macOS development player build succeeded using the installed MacStandaloneSupport module.
- The separate Editor/standalone Relay scene check passed: Services/authentication, Relay allocation
  and join, two-player NGO scene loading, role selection, editing/printing, real 20-second timer,
  help/reputation, matching answers/victory, synchronized return to Lobby and Host disconnection.
- The final loopback fixture passed 52 checks across four rounds plus offline regression. This
  includes duplicate/stale/unauthorized requests, evaluating while the Traveler panel is hidden,
  print lock, wrong-answer failure, 50→30→10→0 reputation failure, result deduplication, round
  resets, a 240-character Inspector reason and a 64-character Unicode identity/answer.
- Gameplay Canvas prefab, existing offline scene, PreparationPreview, PreparationView,
  HackerInterviewView and DraggableDocument were verified unchanged against the starting checkout.

Two computers on different networks, Multiplayer Play Mode's instance launcher, and physical
mouse/keyboard interaction in both windows were not exercised automatically. The automated
checks invoke the same session/gameplay entry points used by the existing controls.

## File inventory

Created (each with its Unity `.meta` file):

- `Assets/Scripts/Howard/CoopGameNetworkBridge.cs`
- `Assets/Scripts/Howard/CoopGameSnapshot.cs`
- `Assets/Scripts/Howard/CoopGameSceneSetup.cs` (editor-only configuration)
- `Assets/Scripts/Howard/CoopGameIntegrationChecks.cs` (editor-only loopback checks)
- `Assets/Scripts/Howard/CoopGameRelayChecks.cs` (editor-only separate-player checks)
- `Assets/Scripts/Howard/CoopGameRelayCheckClient.cs` (opt-in development-player check)
- `Assets/Scripts/Howard/MultiplayerIntegration.md`
- `Assets/Prefabs/CooperativeGameState.prefab`
- `Assets/Scenes/GameScene.unity`

Modified:

- `Assets/Scripts/Howard/NetworkSessionManager.cs` — additive start/return and game-scene cleanup.
- `Assets/Scripts/Howard/LobbyUI.cs` — Start button binding, availability and lobby visibility.
- `Assets/Scripts/Taylor/PreparationSession.cs` — runtime network-mode switch and atomic snapshot overload.
- `Assets/Scripts/Taylor/InterviewController.cs` — request routing, authority evaluation and result snapshots.
- `Assets/Prefabs/MultiplayerCanvas.prefab` — added one Start Game button using existing button styling.
- `Assets/DefaultNetworkPrefabs.asset` — NGO registered the new network-state prefab.
- `ProjectSettings/EditorBuildSettings.asset` — enabled GameScene after MultiplayerTest.

No packages were added or updated by this integration. Pre-existing package-file edits were
preserved. Incidental render-pipeline/player/UnityConnect settings written by Unity during the
validation builds were restored to the starting versions.
