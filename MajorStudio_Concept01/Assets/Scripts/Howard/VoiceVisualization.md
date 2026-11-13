# Hacker Comms voice activity

The gameplay display is wired on `Canvas/DesignViewport/HackerIntervew'sUI/VoiceTimePanel` in `Assets/Prefabs/Taylor/Canvas.prefab`. The Traveler UI, standalone `VoiceUIPreview` lamp-preview scene, lamp shader/material, existing artwork, instruction lettering, slider and panel transforms are unchanged.

## Audio source and permissions

Vivox **16.12.1** is retained. The existing VoiceChatManager remains the only owner of initialization, login, channel joining/leaving, microphone state and T input. Its login now requests `ParticipantPropertyUpdateFrequency.TenPerSecond`, the installed SDK's supported 10 Hz participant-update setting.

The Hacker waveform uses the current room's **self** VivoxParticipant, never a remote participant's energy. The read-only accessor respects the existing actual SDK input mute, user mute, ListenOnly, speech exhaustion and PTT rules. Existing open-voice behavior during the queue is preserved; this display is only authored in the Hacker interview UI. The accessor can distinguish a Traveler's remote source, but no Traveler display is installed, per the requested scope.

`VoiceWaveformVisualizer` reads `AudioEnergy` when `SpeechDetected` is true, the participant is in audio, and not muted. It subscribes to energy, speech, mute and audio-state events; it replaces subscriptions when the participant changes and removes them on disable. Disconnecting or closed comms resets/hides only the new waveform region. The original instruction and existing voice status/lamp handling remain intact.

This is an **energy visualization**, not a raw-audio waveform or frequency spectrum. Deterministic bar variation is scaled by measured energy, so silence produces no artificial peaks. There is no microphone sampling, audio recording, additional Vivox client/channel, or waveform RPC traffic. The renderer never enables the microphone.

## Inspector

All required artwork/region/bar references are assigned. **Voice** is intentionally unassigned on the prefab; the component resolves the existing persistent VoiceChatManager at runtime without creating one.

- **Bar Count:** 1–32, default 28. All 32 Image bars are authored children, reused at runtime.
- **Minimum / Maximum Height:** default 2 / 72 UI units. Maximum is capped to the region height.
- **Audio Gain:** default 3. Increase for quieter signals; decrease if the display saturates too easily.
- **Attack Speed / Release Speed:** default 24 / 7. Higher values respond faster.
- **Smoothing Speed:** default 18, controls individual bar-height interpolation.
- **Bar Color / Idle Color:** active and silent presentation colors.

To reposition the graph within the artwork, edit WaveformRegion's **Anchor Min/Max** in the prefab. Those anchors are captured as the normalized drawing area. The renderer compensates for Image.preserveAspect letterboxing when resized, without editing the background Image's transform. Bars remain centered and do not intercept pointer input.

## Verification

- Unity completed script compilation successfully. Two warnings were reported in unchanged `InterviewController.cs` for unused `wrongAnswerReason` / `reputationFailureReason` fields; no waveform compile warnings/errors.
- `Tools → Voice Visualization → Run Presentation Checks` passed synthetic-energy checks for silence, loudness response, smooth release, pooling, resize/aspect alignment, disconnect/reset and artwork/lamp preservation. These are not microphone tests.
- Serialized comparison confirmed 968 pre-existing objects outside the two attachment points are unchanged. The Hacker panel gains only a component and child reference.
- Static comparison confirmed T input, mute handling, speech counting and queue-round logic are unchanged.
- Actual microphone permission, physical T input, audible transmission, two-peer Vivox energy events and complete multiplayer gameplay regression were not verified for this change.

## Manual two-player check

Restart Play Mode or leave/rejoin before testing so the new login frequency takes effect. Create/join normally, assign Hacker/Traveler, then enter the interview. Focus the Hacker game window, avoid typing in an input field, and hold T. Speak softly then loudly: the bars should follow self energy while the Traveler hears the existing audio. Keep holding T but stop speaking: bars should settle to a flat baseline. Release T, mute, exhaust the existing speech allowance, or disconnect: the waveform should disappear/reset and must not reopen the microphone. Rejoin and repeat.

The existing lamp has an Editor-only T preview. That preview does not simulate waveform energy or prove a live Vivox connection. No manual Inspector assignments are required for the gameplay prefab.
