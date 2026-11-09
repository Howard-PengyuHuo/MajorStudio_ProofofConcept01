# Lobby and cutscene presentation

## Inspector controls

Open `Assets/Prefabs/MultiplayerCutscene.prefab` and select its root `CutsceneCarousel`.

- **Comics → Narration** contains the exact eight supplied segments (3 / 3 / 2). Each segment has editable **Text** and **Duration**, default 4 seconds.
- **Comics → Post Narration Hold Duration** is a separate timer, default 10 seconds per comic. The previously saved three-second holds were changed to the explicitly requested ten-second default.
- **Transition Duration** retains 1.2 seconds and the existing smooth quarter-ellipse animation.
- **Rotation Mode → Comic Only** is the default: comics move, the original Frame stays stationary.
- **Rotation Mode → Comic And Frame** moves each comic with its associated **Rotating Frame**, including matching rotation and depth scale. `Comic1Frame` and `Comic2Frame` are prefab-authored copies of the existing Frame; Comic 3 uses the original `FrameImage`. No frame or UI objects are created by the animation code.
- **Selection Scale Duration** defaults to 0.6 seconds. The Host starts this phase after the final hold; only Comic 3 and the original Frame ease to exactly `(1, 1, 1)`. Selection controls open afterward.

All references are assigned. The original RectTransform values, including the four `(0.8, 0.8, 0.8)` scales, remain unchanged in the prefab. Playback captures these baselines and restores them on replay. `NarrationText` is reused; its font, size, alignment and rectangle are unchanged. The previous selection-status use of this text has been removed so it is cleared/hidden before selection opens. The existing selection title/instructions remain intact.

## Authority

`LobbyRoleState` replicates comic index, narration segment index, phase, sequence and Host timestamps as one server-written value. Only the Host advances segment deadlines, then the separate hold, then the selection scale phase. Clients render the latest replicated segment and interpolate visuals locally. Stale, premature and Client-originated narration completion calls cannot advance the timeline. The selection/countdown validation remains Host-authoritative.

Both peers must run rebuilt versions of this change: the serialized network state now includes a segment index and an additional phase.

## Lobby background investigation

The saved scene has the exact P2 sprite reference and binds the actual `LobbyPanel` passport Image. `JoinRoom` sets `Connected` after NGO confirms connection, then raises `StateChanged`; `IsHost` comes from the local NetworkManager. No Animator or competing background writer was found on this Image. The open Editor scene had cleared P1/Image Inspector fields (the existing runtime binding/fallback still worked); those references were restored to the saved scene's assignments.

The reported failure did **not** reproduce with the current source: a fresh standalone Client using the real Relay Join Code displayed `Lobby Main Interface_Player2_0` on join, return from gameplay, and rejoin, even before the extra refresh safeguards. This does not establish why a previous running player showed the wrong image; stale builds or different Inspector state remain possibilities, not confirmed causes.

`LobbyUI` now also refreshes on player updates and immediately before a visible Canvas renders, rebinds a stale Image reference to its actual local panel, clears a conflicting uGUI sprite override, and reports missing references instead of silently ignoring them. Host/Client identity, not player count, still determines the sprite. No layout or artwork is changed. Rebuild both players before retesting an older executable.

## Validation

- Unity Editor completed successful script compilation with no reported warnings/errors.
- `Tools → Multiplayer Cutscene → Run Loopback Checks (Play Mode)` passed 186 checks with real NGO/Unity Transport Host and Client peers. Coverage includes exact rendered segments, Host-configured segment/hold durations, both carousel modes for 1→2 and 2→3, frame draw order, scale completion/reset, narration fitting the existing rectangle, hover, marker ownership, deselection, countdown cancellation/restart, and gameplay role mapping. This fixture is not Multiplayer Play Mode and uses a temporary identity swap on the authored UI for the local background assertions.
- A fresh macOS development build and the existing separate Editor/standalone Relay fixture passed real Join Code connection, default-duration introduction, selection/countdown, GameScene loading with Host Hacker / Client Traveler, return, disconnect/rejoin and Host shutdown. Client background, visible Image override and two-player count were checked after join/return/rejoin.
- Static checks verified all eight narration strings match the supplied request exactly and every original prefab RectTransform's position, anchors, pivot, size, rotation and scale is unchanged.
- Multiplayer Play Mode UI-driven testing and two-physical-computer visual review were not performed. The Relay test runs two separate processes on one Mac; it does not prove cross-network connectivity by itself.

For manual Multiplayer Play Mode testing, configure the Editor plus one additional Editor instance in **Window → Play Mode → Scenarios**. Create a room in one, enter its code in the other, check P1/P2 backgrounds and `2 / 2`, then Start Game. Repeat with each Rotation Mode, leave/rejoin, and swap which instance hosts. Review motion visually at the production timings; automated transform checks do not replace subjective animation review.
