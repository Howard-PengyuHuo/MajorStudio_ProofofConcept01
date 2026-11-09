# Multiplayer cutscene and character selection

## Current flow

1. Open MultiplayerTest and create/join the same Relay room.
2. The custom Lobby shows P1 = Host and P2 = Client connection status. No roles or Ready controls.
3. Exactly two connections enable the Host's existing Start Game button.
4. Start plays three comics with rotating carousel entrances. Each entrance finishes before its
   narration hook fires. Prototype narration completes immediately on both peers, then the server
   waits that comic's configured post-narration hold (10 seconds by default).
5. After the final hold, character selection appears beside the final comic.
6. Person maps to Traveler; Briefcase maps to Hacker. Either network player can choose either.
   Click your selected character again to deselect. Choose another unoccupied character to switch.
   Occupied characters cannot be taken. P1/P2 markers always identify Host/Client, not fixed roles.
7. Two distinct accepted choices automatically start a 10-second server-timed countdown.
   A successful change/deselection cancels it; the stopwatch disappears. A new valid pair starts
   a fresh full-duration countdown. Rejected or identical requests do not reset it.
8. Only the latest valid countdown may load the existing GameScene additively through NGO.
   The persistent role state supplies PreparationPreview's local role.
9. Return To Lobby (Host component context menu) resets roles and allows replay without Ready.
   Leave Room works throughout the introduction. A pre-game Client departure cancels progression
   and returns Host to the custom Lobby; a replacement pair replays the comics.

Taylor's scripts, Canvas, offline scene, and gameplay mechanics are unchanged. The existing
CooperativeGameState prefab has gameplaySynchronizationEnabled enabled in the current checkout;
that setting is preserved. This update enters its existing gameplay flow and supplies the chosen
roles without redesigning preparation/interview mechanics. Voice and Relay setup are unchanged.

## Editable prefab and Inspector settings

Open Assets/Prefabs/MultiplayerCutscene.prefab in Prefab Mode:

- Root / CutsceneCarousel / Comics: ordered Image references and a separate
  Post Narration Hold Duration per comic, default 10 seconds. Add/remove/reorder both the saved
  comic Image objects and array entries together.
- Transition Duration: 1.2 seconds by default; Carousel Radius controls the rotating arc.
  Comic Image RectTransforms define editable center positions/sizes; their authored rotation is
  retained as the center pose.
- Content/FrameImage: your authored Image and assigned artwork behind the comics are preserved.
  Replace Source Image and adjust RectTransform freely; the interaction update does not modify it.
- Content/CharacterSelection/StopwatchImage: regular Image, temporary gold square, no Sprite.
  Replace Source Image and edit RectTransform. RemainingTime is its noninteractive Text child.
- PersonButton retains your Journalist 1.png sprite; BriefcaseButton retains Suitcase.png.
  CharacterHoverFeedback on each button shows the existing JournalistDescription / BriefcaseDescription
  Image and applies the shared CharacterHover material only to that button's Image. Exit, disable,
  focus loss or hovering the other character restores the original material and hides the description.
  Hover never submits network requests. Both character Buttons use Transition=None to prevent
  UI focus from becoming a persistent highlight.
- PersonMarker and BriefcaseMarker are now ordinary Image children above their respective buttons.
  CutsceneCarousel references both Images and the existing P1.png / P2.png sprites. Server-confirmed
  Host selection displays P1, Client selection displays P2, and unselected markers are inactive with
  no sprite. There is no marker Text or unselected-status label. Description and marker Images have
  Raycast Target off so they do not intercept clicks or create hover flicker.
- Automatically Complete Narration: enabled for this prototype. Do not disable without providing
  completion handlers on both peers.
- Comic Narration Requested: Inspector UnityEvent<int> for future text/audio presentation.
  C# NarrationRequested(int index, uint sequence) exposes a safe asynchronous completion token.

Open Assets/Prefabs/LobbyRoleSelection.prefab:

- Countdown Duration: serialized server-authoritative setting, default 10 seconds.
- Cutscene Prefab: already assigned to MultiplayerCutscene.

All visual elements are saved prefab objects. Runtime only instantiates the prefab and updates
its visibility, positions and text; it does not construct UI graphics. The frame Image and its
RectTransform are also exposed through FrameImage and FrameRectTransform properties.
Configure Assets under Tools > Multiplayer Cutscene only assigns the existing authored prefab to
the role object. It no longer generates UI or replaces prefab artwork/layout. Restore the authored
prefab from version control if it is missing.

## Narration integration contract

OnComicNarrationRequested(int comicIndex) contains the TODO integration hook. Subscribe to
NarrationRequested and capture its index AND sequence in each playback operation. When that
operation finishes, call NotifyNarrationCompleted(comicIndex, sequence) on that carousel.
Do not fetch a newer ActiveNarrationSequence inside a delayed callback. Completion is accepted
only for the current comic/sequence/narration stage and an actual connected sender. Duplicate
acknowledgments are harmless. Host waits for both peers, then starts the per-panel hold. The
prototype automatically invokes the completion entry point, so no unimplemented callback stalls it.

## Authority and cancellation

One persistent server-written LobbyRoleState synchronizes network slots, character assignments,
comic phase/index/sequence, phase timestamps, countdown end time/generation, and game-start lock.
RPCs derive sender identity from NGO, check membership, role occupancy, enum validity, player
version and request ID. No RPC accepts a target player ID or arbitrary countdown duration.
No Ready API or player-facing countdown controls remain.

Accepted role changes invalidate the previous countdown generation. Server Update evaluates the
current state rather than running delayed countdown tasks. At expiry TryLockForStart rechecks the
generation, active deadline, two connections and distinct valid roles, then locks BEFORE scene load.
Client display time is calculated from NGO ServerTime.Time, never an independently started timer.
The role object survives the additive scene transition and supplies the unchanged gameplay bridge.

## Tests and repeatable commands

- Tools > Multiplayer Cutscene > Run Loopback Checks (Play Mode): two real NGO/Unity Transport
  peers in a fresh idle MultiplayerTest Play session. Short timings apply only to runtime fixture
  instances, never to saved prefab defaults. Covers role conflicts, invalid requests, mutable
  selection, synchronized timer visibility/deadline, stale narration/timer rejection, role swaps,
  final start lock, PreparationPreview mapping and disconnect/rejoin.
- Tools > Multiplayer Cutscene > Build Check Player (macOS), then fresh Play Mode and
  Run Relay Scene Checks (Play Mode): Editor Host plus temporary standalone Client through Relay.
  Uses the actual default comic holds/countdown, invokes bound buttons, cancels a countdown and
  waits past its former deadline, then reselects and checks NGO GameScene loading and both roles.
  Also checks return-to-Lobby and both disconnect directions. Temporary logs/screenshots are in
  the evidence folder printed by the test.
- Stop Play Mode between fixtures. Do not recompile during a test (domain reload is disabled).
- One-Mac manual test: run Editor and standalone player, Create on one, Join by code on the other.
  Start with two players, wait for selection, try conflicts/deselect/reselect and verify both timers.
- Internet test: run matching builds on two computers on different networks, join the Relay code,
  and repeat the same flow. No port forwarding. Two physical networks remain a separate manual test.

### Results for this update

- Unity 6000.5.4f1 compilation completed: zero errors and zero warnings.
- Real NGO/UTP loopback: 53 checks passed.
- macOS development-player build succeeded. Separate Editor Host / standalone Client through
  actual Unity Relay passed: default-duration comics, button selections and deselection, countdown
  cancellation past the old deadline, fresh automatic countdown, NGO GameScene loading, selected
  Host Hacker / Client Traveler panels, return to Lobby, disconnect/rejoin and Host shutdown.
  The initial run exposed an outdated test assumption that gameplay was disabled; the assertion
  was corrected to preserve the existing enabled gameplay mode, then the full run passed.
  Evidence: /var/folders/1g/qknmgd2122v_t7yf31jwjcdh0000gn/T/coop-relay-55514a114a4d4434976bfdc59ce9f376
  (role-client.log, role-client-result.txt, lobby.png, character-selection.png).
- Captured and inspected the actual Lobby and character-selection screenshots; corrected the
  new prefab's title to display the full existing texture and made status/markers legible.
- Physical mouse/keyboard interaction across two windows and two-computer/different-network test
  are not claimed as tested by automated button invocation.
- Lobby serialized-object comparison: removed only the 64 objects/components belonging to the old
  role/Ready sidebar. All 377 surviving objects/components are byte-identical except the Canvas
  root's child list. No surviving handmade artwork/layout object changed.

## Changed files

New: CutsceneCarousel.cs, CutsceneSetup.cs (both under Assets/Scripts/Howard/) and their .meta files;
Assets/Prefabs/MultiplayerCutscene.prefab and .meta.

Modified scripts under Assets/Scripts/Howard/: LobbyRoleSelection.cs, LobbyRoleState.cs,
NetworkSessionManager.cs, LobbyUI.cs, LobbyRoleSelectionSetup.cs, LobbyRoleSelectionChecks.cs,
LobbyRoleRelayChecks.cs, LobbyRoleRelayClient.cs.

Modified prefabs: Assets/Prefabs/LobbyRoleSelection.prefab (cutscene reference and countdown setting);
Assets/Prefabs/MultiplayerCanvas.prefab (removed obsolete sidebar only).

Documentation: this file and MultiplayerIntegration.md. No package, scene, build configuration,
Taylor gameplay, source artwork, or gameplay prefab changes are required.

## Hover and image-marker update

The Lobby root cause was nearly black Button Color Tint values (0.102 for hover and pure black
for Leave Room's selected state), not missing or swapped sprites. Copy Code, Start Game and Leave
Room now use the same tint block: normal 1, hover/selected 0.88, pressed 0.74, disabled 0.72,
alpha 1, multiplier 1, fade 0.08 seconds. Their existing artwork Images remain the Target Graphics.
Only those three Button components changed in the Lobby prefab; all sprites and layout remain intact.

CharacterHoverFeedback is local-only. Each button references its own Image, the shared immutable
CharacterHover material, its existing description GameObject, and the other button's feedback
component. The shader preserves sprite alpha and brightens black silhouettes; ordinary multiplicative
Color Tint cannot brighten black pixels. It never changes Sprite, RectTransform, comic material or
network state. Material Hover Color / Hover Strength are editable; the default is a warm highlight
at strength 0.6. Original materials are restored on pointer exit, disable, focus loss or peer hover.

The existing PersonMarker/BriefcaseMarker GameObjects were reused, their Text components replaced
with Image, and parented above the corresponding character Button (48x48, top-center, offset 20).
CutsceneCarousel's Player One Marker Sprite references P1.png's p1_0 subasset, and Player Two Marker
Sprite references P2.png's P2_0 subasset. A marker is inactive with a null sprite when unselected.
Only accepted server state chooses which marker to display. Click own character to release it;
click someone else's character does nothing; either player can claim a released character.

No manual Inspector assignment is required. All image, description, material, peer-hover and marker
references are serialized in MultiplayerCutscene.prefab. Existing description names (including the
trailing space on BriefcaseDescription) and their artwork/layout were preserved.

Files changed by this interaction update (distinct from the earlier cutscene implementation):

- Assets/Prefabs/MultiplayerCanvas.prefab
- Assets/Prefabs/MultiplayerCutscene.prefab
- Assets/Scripts/Howard/CutsceneCarousel.cs
- Assets/Scripts/Howard/CutsceneSetup.cs (removed obsolete UI generation)
- Assets/Scripts/Howard/LobbyRoleSelectionChecks.cs
- Assets/Scripts/Howard/LobbyRoleRelayClient.cs
- Assets/Scripts/Howard/RoleSelection.md

New files: Assets/Scripts/Howard/CharacterHoverFeedback.cs and .meta;
Assets/Shaders/CharacterHover.shader and .meta; Assets/Materials/CharacterHover.mat and .meta;
the Shaders and Materials folder .meta files. No source images, scenes, packages, server role
rules, countdown logic or gameplay mechanics were changed by this interaction update.

Validation: Unity compilation completed with zero C# errors/warnings. Expanded loopback tests
passed 101 checks over two actual NGO/UTP peers, including all three Lobby buttons' hover/pressed/
disabled artwork, local-only exclusive hover, description cleanup, unchanged sprites, image markers,
P1 claim/cancel then P2 claim, direct role switching, and the prior countdown/disconnect regressions.
Captured and visually inspected both character hover states and Lobby button states. Evidence folder:
/var/folders/1g/qknmgd2122v_t7yf31jwjcdh0000gn/T/character-ui-fe2b3b283ff64754a74071d58b0c8ca7
Pointer/button actions were automated through Unity's event handlers; physical mouse interaction
and two computers on different networks are not claimed as tested.

The updated macOS development player built successfully, including the highlight shader. The
separate Editor/standalone Relay regression also passed, including the Client's P2 image marker,
button selection/deselection, countdown cancel/restart, GameScene role mapping, return to Lobby,
disconnect/rejoin and Host shutdown. Evidence folder:
/var/folders/1g/qknmgd2122v_t7yf31jwjcdh0000gn/T/coop-relay-83551b0b1d3d4ce897c67a7d63d02500
Unity was returned to Edit Mode; incidental build-generated font-cache and preloaded-assets
changes were restored. All unrelated pre-existing work, including your new character art, remains.
