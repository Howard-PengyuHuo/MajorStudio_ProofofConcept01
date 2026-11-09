# Multiplayer role selection

This is the current online stage: Lobby role selection, Ready, synchronized GameScene loading,
and the selected local role's existing UI in a non-interactive Waiting phase.
The older gameplay synchronization hooks are retained but disabled on the CooperativeGameState
prefab. No countdown, identity/print, interview, answer, reputation or result networking runs.
Taylor's Canvas prefab, offline scene and gameplay scripts are unchanged by this stage.

## Use

1. Open `Assets/Scenes/MultiplayerTest.unity` and enter Play Mode.
2. Create a Relay room on one instance. Copy its code and join from the other instance.
3. Host defaults to Traveler and Client to Hacker. Both see server-confirmed assignments,
   connection status and Ready state in the existing MultiplayerCanvas's new sidebar.
4. To swap: cancel Ready, click **RELEASE ROLE**, then choose the other role once its occupant
   also releases it. An occupied role cannot be taken. Release does not leave the room.
5. Both players click **READY**. **CANCEL READY** permits further role changes.
6. **START GAME** is visible only to Host and becomes interactable only with two connected,
   assigned, distinct, Ready players. Host validates everything again before locking the start.
7. NGO loads GameScene additively on both peers. Each sees only its chosen Traveler or Hacker
   preparation panel. The existing local preview shortcuts and automatic preparation are inactive.
   Remaining in Waiting is intentional for this stage.
8. In the Host's NetworkSessionManager component menu, **Return To Lobby (Host)** unloads
   GameScene for both peers, retains roles, and resets both Ready states. **Leave Room** retains
   the original disconnect behavior. A disconnected player's role is released and the survivor
   becomes Not Ready. A replacement client receives whichever role is free if the survivor has
   already selected Hacker.

No Inspector assignment remains. `Tools > Role Selection > Configure Assets` is an explicit,
idempotent repair command that configures the prefab references and adds missing sidebar UI.
All new controls are ordinary saved prefab objects and editable in Unity Prefab Mode.

## Authority and lifecycle

`LobbyRoleSelection` is one server-owned persistent NetworkObject, instantiated from
`Assets/Prefabs/LobbyRoleSelection.prefab` after StartHost. NGO spawns its counterpart on Client.
Each NetworkManager's own NetworkSessionManager registers its local counterpart; there is no
global lookup or independent local role ownership.

One server-written NetworkVariable contains Host/Client IDs, connection flags, roles, Ready
flags, per-player versions and the game-start lock. RPCs derive identity from the transport's
actual sender; they accept no target-player ID. Host checks membership, the sender's current
selection version, Ready lock, role occupancy and the scene/game lock. Per-player versions
permit simultaneous Ready requests while rejecting stale actions from before a role change.
Request IDs reject duplicates. Confirmation updates feedback; pending controls recover after
a bounded timeout if confirmation does not arrive.

NetworkSessionManager's CanStartGame and StartGame both use this authoritative state.
The start lock is set before invoking NGO's existing additive LoadScene, preventing duplicate
starts. Roles cannot change during loading or gameplay. The role object survives the scene
transition and return to Lobby; it is despawned with the session.

CoopGameNetworkBridge waits for its local session's authoritative role data before activating
its one existing gameplay Canvas copy. It subscribes to LobbyChanged for late data and calls
PreparationPreview.SetLocalRole with the selected role. PreparationSession.EnterNetworkMode
disables local simulation before the prefab is activated; the bridge applies a blank Waiting
snapshot and subscribes to none of the gameplay request events in this stage. F1/F2 are already
guarded by LocalSimulation in the existing preview. Network listeners are removed on despawn.

## UI preservation

The sidebar is a sibling of LobbyPanel, so none of the original panel positions, anchors,
sizes, font settings or LayoutGroup properties need to change. Its own RectTransform defaults
to 300×620, left of the existing central Lobby panel at the Canvas's 1280×720 reference size.
Its new text/buttons copy the existing prefab's style. Customize only these new objects as
desired in Prefab Mode. Existing Create/Join/Copy/Leave and Start controls are reused.

## Repeatable checks

- Enter a fresh Play session in an idle MultiplayerTest Lobby and select
  **Tools > Role Selection > Run Loopback Checks (Play Mode)**. This uses two actual NGO/Unity
  Transport peers and checks role uniqueness, release/swap, Ready/cancel, invalid/unauthorized
  actions, Start gating/locking, disconnect/rejoin, selected role panels, delayed role data,
  one Canvas per peer and Waiting-only gameplay. Stop Play Mode after the fixture completes.
- In Edit Mode, **Build Check Player (macOS)** builds a temporary development player using the
  existing build helper. Enter a fresh Play session and select **Run Relay Scene Checks
  (Play Mode)**. This launches the separate player, exercises actual Relay joining, swaps roles,
  readies both peers, invokes the bound Start Game button twice, checks synchronized role-based
  waiting UI, returns to Lobby, disconnects/rejoins Client, then closes Host.
- Do not recompile scripts during a test. With domain reload disabled, stop/re-enter Play Mode
  after compilation so NGO's runtime initialization runs from a clean scene.
- The test client attaches only when explicit `--role-check-*` arguments are supplied and is
  excluded from non-development builds. Normal launches do not perform test actions.

For Multiplayer Play Mode 2.0.2, open **Window > Play Mode > Scenarios**, configure the main
Editor and one additional Editor, and use the manual Relay-code workflow above. For actual
internet verification, run the same build on two computers on different networks.

## Files

Created (with Unity `.meta` files): LobbyRoleState.cs, LobbyRoleSelection.cs,
LobbyRoleSelectionSetup.cs, LobbyRoleSelectionChecks.cs, LobbyRoleRelayClient.cs,
LobbyRoleRelayChecks.cs and this guide, all under `Assets/Scripts/Howard/`;
also `Assets/Prefabs/LobbyRoleSelection.prefab` and its `.meta` file.

Modified: NetworkSessionManager.cs, LobbyUI.cs, CoopGameNetworkBridge.cs, the MultiplayerCanvas
and CooperativeGameState prefabs, MultiplayerTest scene, DefaultNetworkPrefabs registry,
and the older integration guide's current-stage note. No packages are added or upgraded.

## Verification performed (2026-10-29)

- Unity 6000.5.4f1 compiled the changed scripts with zero errors and zero warnings.
- The loopback fixture passed 50 checks over actual NGO/Unity Transport messages, including
  selection/release, occupied/invalid-role rejection, Ready/Cancel Ready, start locking,
  swapped role panels, delayed role-data availability, Waiting-only presentation and reconnect.
- A macOS development build succeeded. The separate Editor/standalone Relay fixture passed:
  room creation/join, role swap, Ready gating, bound Start-button/duplicate-start handling,
  synchronized GameScene load with Host=Hacker and Client=Traveler, no duplicate runtime
  objects, return to Lobby with roles retained/Ready cleared, Client disconnect/rejoin,
  and Host disconnect cleanup. Both final test runs reported no Console errors or warnings.
- All 134 original lobby prefab object/component records were retained. The only changed
  original record is the Canvas root's child list, which adds the new sidebar. Original UI
  positions, sizes, fonts, colors and layout settings are unchanged. Taylor assets/scripts
  and the offline scene were verified unchanged against the starting checkout.

The Multiplayer Play Mode launcher itself was not exercised: native Unity UI inspection
stalled, so verification used the Editor bridge and a separate standalone player instead.
Two physical computers/networks and physical mouse/keyboard interaction in both windows
remain untested. The automated Relay fixture invokes the actual bound Start Game button.
