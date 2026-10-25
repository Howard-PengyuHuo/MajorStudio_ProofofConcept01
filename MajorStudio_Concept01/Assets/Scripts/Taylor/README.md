# CarryOn — Taylor preparation prototype

## Current workflow — manual UI authoring

The user requested manual scene UI authoring. The generated TaylorPreparation hierarchy, both role UI prefabs and the two editor UI generation scripts have been removed. Do not regenerate UI or reposition their manually arranged objects.

The user's Canvas now has PreparationSession and PreparationPreview. Their existing role panels have PreparationView with Inspector references bound to their regulation images, timers and four input fields. Status labels are optional. Play automatically starts the local 60-second queue and defaults to Hacker. Focus the Game view and use F1 for Traveler / F2 for Hacker; switching keeps the shared timer and identity. On Canvas, configure Preparation Seconds, Auto Start and Starting Role in the Inspector. Stop and restart Play to reset. Timeout locks inputs, hides traveler regulations and shows hacker regulations; interview UI is still pending. No UI objects are generated at runtime. Verified input-change events, switching and timeout in Play mode without new runtime errors. The older prototype instructions below are historical.

## Agreed scope

Current reputation/result behavior: InterviewController on Canvas starts reputation at 50 and deducts 20 per accepted traveler Shake click (50 -> 30 -> 10 -> 0). Zero ends entry; one wrong answer also ends entry; all three correct approves entry. The existing shared GameOverUI uses result and reason text for all outcomes. Both role UIs are hidden after the result, answer/shake actions are locked, and F1/F2 cannot reopen them. Inspector fields Starting Reputation, Shake Penalty, and result messages remain editable. On Shake Requested is a hook for later feedback/network integration; no screen shake or hacker notification visual is implemented. Verified all three outcomes and post-result action blocking in local Play mode. These newer rules supersede earlier notes saying reputation was pending.

Hacker interview desktop: the user's existing HackerIntervew'sUI/DocumenArea holds RegulationImage and IDDocument. Both use DraggableDocument: left press brings the selected document to the last sibling (front), drag moves in parent-local coordinates preserving the grab offset, release stops movement. Child text follows the card. No runtime UI generation or network position synchronization; desktop arrangement is local. HackerInterviewView binds the four existing labels to the printed identity (or current locked identity when no print happened). PreparationPreview switches to this desktop after queue expiry; F1/F2 retain the existing local role controls. Positions and sibling order persist during role switches and reset on a new Play session. Verified event handlers, scaled-canvas movement without initial jump, both documents coming to front, identity display and phase visibility in Play mode.

Print: the existing hacker PrintButton is bound through PreparationView. All four fields must be nonblank. Print finalizes the current identity, locks editing and displays a read-only PrintedID under the traveler's preparation panel. The countdown continues. The static scene copy was explicitly authorized by the user and can be manually repositioned; no runtime UI creation is used. The card is hidden until Print, hidden at the checkpoint, and cleared for a new preparation round. Missing Print at timeout retains the earlier demo behavior (no extra automatic loss). For future networking, handle PrintRequested on the authority, validate hacker ownership, call PrintIdentity, then replicate HasPrintedIdentity/PrintedIdentity via ApplyPrintedIdentitySnapshot along with normal phase/identity state. Send a false print snapshot on a new network round. Printing does not yet perform any real network transmission.

Latest demo decision: three interview questions, not five. The manually built interview panel has a question label, single-line answer input, numeric Reputation and a shake button; no progress, submit or feedback UI. Enter submits through TMP onSubmit. On Canvas, InterviewController > Questions edits question text, answer source (FixedAnswer/ForgedName/ForgedNationality), and fixed answer. Defaults: full name / forged Name, nationality / forged Nationality, visit purpose / `believer`. Matching ignores case and outer whitespace only; empty submission is ignored. A wrong answer ends entry immediately; all correct shows ENTRY APPROVED in the existing question label. No additional UI is generated. F1 selects the traveler panel appropriate to the current phase; F2 selects hacker. Reputation and shake behavior are not implemented yet. This is local demonstration logic; multiplayer authority integration is pending.

Two-player online cooperation; networking is owned by a teammate. During the shared queue countdown the traveler memorizes entry regulations and the hacker types a forged identity. At the checkpoint the traveler types answers; the current target is five questions. One wrong answer fails entry. Shaking the bag requests help, plays screen shake and increases suspicion; reaching the threshold fails entry. The hacker can consult the identity and regulations at the checkpoint and give spoken hints offline. In-game voice is out of scope for this prototype.

## Implemented now

Open `Assets/Scenes/Taylor‘sScene.unity`, enter Play mode, click START QUEUE. TRAVELER / HACKER switch the local preview role while retaining the shared timer and identity. Stop and re-enter Play mode to reset.

Select `TaylorPreparation` and edit `PreparationSession` in the Inspector:

- Preparation Seconds: 60 by default.
- Local Simulation: enabled for single-editor testing; disable when using a network authority.
- Hacker Can Read Rules During Preparation: false by default, pending design confirmation.

Traveler and hacker UI prefabs live in `Assets/Prefabs/Taylor`. Their text, images and layout are placeholders for the art team. Scene instances bind to the shared session. Prefab assets intentionally contain no scene reference; assign/bind the session before enabling a newly instantiated view.

Input is saved on each change and locked when preparation ends. All four fields are free text (64 characters maximum). No identity validity rules are invented yet. An incomplete card proceeds to the interview placeholder without an automatic loss. The traveler loses access to regulations at the checkpoint; the hacker gains access. No question, shake, suspicion or win/loss implementation is included in this first stage.

## Network integration contract

`PreparationSession` has no networking-package dependency. It is a local prototype model, not a completed networking system.

- With Local Simulation disabled, `RequestStart` emits `StartRequested`; `RequestIdentityEdit` emits `IdentityEditRequested`.
- Adapter validates the sender's role and phase on the authority; only the hacker should edit identity. Local preview switching is not authorization.
- Authority owns countdown deadline, identity and phase. Both clients derive/display time from the authoritative clock; do not start independent local timers.
- Apply authoritative state through `ApplyNetworkSnapshot(phase, remaining, card)`. `Changed` refreshes UI. `PreparationCompleted` fires once on transition to Interview.
- `PreparationPreview.SetLocalRole` selects a local role. Remove/replace the preview component for production role assignment; its Start currently selects Traveler. Debug controls are hidden when Local Simulation is off.
- `BeginPreparation` and `ApplyIdentityEdit` are authority-side entry points; they do not validate network sender ownership themselves.
- `AdvanceLocalTime` is for local simulation only. With it disabled, the network adapter must advance the authoritative deadline and send the Interview state.

## Pending design decisions

Five exact questions and matching rules; whether the hacker sees regulations during preparation; missing-field outcome; whether both screens shake; suspicion increments and threshold. Identity locking after timeout is the current prototype behavior. ID art has name/nationality/age/license number; rules also refer to occupation, purpose and credentials, so question design still needs alignment.

## Verification

Unity 6000.5.4f1: scripts compile without errors. In Play mode verified advancing frames; start-button event; all four input change events; role switching preserves input and timer; timeout locks input and ignores later edits; completion fires once; traveler hides regulations and hacker shows them; one EventSystem; automatic elapsed-time transition; incomplete card reaches placeholder. Screenshots inspected at 1280x720. Console reported no runtime errors. Actual two-client networking and physical keyboard/mouse end-to-end testing are still to be performed.
