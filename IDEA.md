# 💎 Crystal Relay — Ideas & Future Concepts

This document is a collection of ideas, experiments, and possible future directions for **Crystal Relay**.

Crystal Relay connects Twitch interactions with VRChat through OSC/OSCQuery, allowing stream events such as Channel Points, Bits, subscriptions, chat commands, payments, and other triggers to control VRChat avatar parameters and streamer interactions.

> [!NOTE]
> Ideas listed here are **not guaranteed features** and should not be treated as a roadmap or promised release schedule.

---

## 🎯 Goals

Ideas for Crystal Relay should generally support one or more of these goals:

* Make VRChat streams more interactive.
* Give viewers fun ways to affect the stream.
* Make complicated OSC setups easier to configure.
* Reduce repetitive setup for streamers.
* Keep interactions understandable and recoverable.
* Avoid leaving avatars or OSC parameters in broken states.
* Give creators flexible tools without requiring programming knowledge.
* Keep Crystal Relay free and open source.

---

# 💡 Feature Ideas

## 🎲 Advanced Redeem Randomizer

Create a more powerful randomization system for redeems.

Possible options:

* Random avatar
* Random outfit
* Random avatar parameter
* Random movement
* Random avatar scale
* Random combination of actions
* Weighted outcomes
* No-repeat mode
* Cooldowns for individual outcomes
* Separate viewer-specific random pools

Example:

```text
Viewer redeems "Chaos Crystal"

Possible result:
→ Avatar Swap
→ Tiny Avatar
→ Random Outfit
→ Spin for 10 seconds
→ Random Float Parameter
```

---

## 🔗 Action Chains

Allow multiple actions to be connected into a sequence.

Example:

```text
Channel Point Redeem
        ↓
Change Avatar
        ↓
Wait 2 seconds
        ↓
Enable Parameter
        ↓
Spin
        ↓
Wait 10 seconds
        ↓
Restore Previous State
```

Possible actions:

* Change avatar
* Set parameter
* Modify float
* Trigger movement
* Change scale
* Send Twitch message
* Wait
* Play another Crystal Relay action
* Restore previous state

---

## 🧩 Visual Trigger Builder

A visual system for building advanced triggers without manually configuring every action.

Example:

```text
[Twitch Reward]
      ↓
[Check Current Avatar]
      ↓
[Set Parameter]
      ↓
[Wait 15 Seconds]
      ↓
[Restore Parameter]
```

Possible nodes:

* Trigger
* Condition
* Action
* Delay
* Randomizer
* Counter
* Cooldown
* Restore
* Branch

---

## ⚡ Combo Redeems

Allow multiple viewers to contribute toward a larger effect.

Example:

```text
Crystal Explosion

Goal: 5 redeems

1/5 ████░░░░░░░░░░░░
2/5 ████████░░░░░░░░
3/5 ████████████░░░░
4/5 ███████████████░
5/5 ████████████████

ACTIVATED
```

Possible triggers:

* Channel Points
* Bits
* Subs
* Donations
* Chat commands
* Mixed contribution types

---

## 📈 Stream Interaction Meter

Create a live interaction meter that grows as viewers interact.

Possible contributions:

* Bits
* Subs
* Channel Point redeems
* Donations
* Chat activity
* Follows

Example stages:

```text
0%   Calm
25%  Unstable
50%  Chaotic
75%  Critical
100% CRYSTAL OVERLOAD
```

Each stage could activate different OSC actions.

---

## 💥 Crystal Overload Mode

A special temporary chaos mode triggered when a goal is reached.

Possible effects:

* Random movement
* Avatar scaling
* Random parameters
* Rapid outfit changes
* Avatar roulette
* Viewer-controlled actions
* Temporary enhanced redeems

After the timer ends, Crystal Relay restores everything to its previous state.

---

## 👑 Boss Battle Mode

Allow chat to collectively fight a configurable "boss."

Example:

```text
VOID CRYSTAL

HP
████████████████████ 100%

Bits        = damage
Subs        = critical damage
Redeems     = abilities
Donations   = special attacks
```

Boss phases could trigger VRChat effects.

Example:

```text
75% HP → Avatar parameter activates
50% HP → Avatar scale changes
25% HP → Movement effects begin
0% HP  → Victory action chain
```

---

## 🧑 Viewer Profiles

Optionally remember interaction information for viewers.

Possible data:

* Number of redeems
* Bits contributed
* Subscription triggers
* Favorite interactions
* Last triggered action
* Viewer-specific cooldowns

Potential use:

```text
Viewer reaches 100 interactions
        ↓
Unlock special redeem
```

Privacy controls would be important for this feature.

---

## 🎟️ Viewer Unlocks

Allow viewers to unlock special interactions after meeting configurable conditions.

Examples:

* Follow duration
* Subscription tier
* Number of redeems
* Bits contributed
* Stream interaction milestones

Possible unlocks:

* Special avatar roulette pool
* Rare outfit
* Special parameter
* Unique movement effect
* Custom command

---

# 🤖 Automation Ideas

## 🕒 Scheduled Actions

Run actions automatically at configured intervals.

Examples:

```text
Every 30 minutes:
→ Trigger Hydration Reminder

Every 2 hours:
→ Run Avatar Roulette

At stream ending:
→ Restore Default Avatar
```

---

## 👤 Avatar-Aware Rules

Allow rules to behave differently depending on the active avatar.

Example:

```text
IF Avatar = Dragon
    Redeem → Fire Breath

IF Avatar = Robot
    Redeem → Error Mode

IF Avatar = Cat
    Redeem → Cat Ears Toggle
```

---

## 🌎 World-Aware Actions

Use VRChat/OSCQuery information when available to change behavior depending on the current environment.

Potential uses:

* Disable risky movement effects in certain situations.
* Change available actions depending on the world.
* Automatically activate streamer presets.

Any implementation should respect VRChat privacy and safety expectations.

---

# 🎛️ OSC Ideas

## Parameter Inspector

A developer/debug window showing live OSC parameter activity.

Example:

```text
Parameter                       Value

/HeadPat                        1
/Outfit                         3
/ExpressionHappy                true
/avatar/eyeheight               1.42
```

Useful actions:

* Search parameters
* Pin parameters
* Copy OSC address
* Watch value changes
* Manually test values
* Export parameter list

---

## OSC History

Maintain a temporary history of OSC changes.

Example:

```text
14:32:10  Outfit       0 → 2
14:32:14  HeadPat      0 → 1
14:32:15  HeadPat      1 → 0
14:32:21  EyeHeight    1.70 → 0.90
```

This could help troubleshoot complicated setups.

Sensitive information should never be written into diagnostics unnecessarily.

---

## Parameter Conflict Detection

Warn when multiple active Crystal Relay systems are attempting to control the same parameter.

Example:

```text
⚠ Parameter Conflict

Parameter:
Outfit

Currently controlled by:
• Avatar Set
• Universal Trigger
• Supporter Trigger
```

Possible resolution:

* Highest priority wins
* Queue actions
* Pause previous action
* Ask user
* Ignore warning

---

# 🛡️ Safety & Recovery Ideas

## Emergency Reset

A clearly visible action that immediately attempts to restore Crystal Relay-controlled state.

Possible reset targets:

* Movement
* Avatar scale
* Float parameters
* Bool parameters
* Active timers
* Avatar swaps
* Queued actions

---

## Stream Safe Mode

Temporarily disable potentially disruptive interactions while keeping Crystal Relay connected.

Example:

```text
SAFE MODE

✅ Twitch Connected
✅ VRChat Connected

⛔ Movement Redeems
⛔ Avatar Swaps
⛔ Scaling
⛔ Random Actions
```

---

## Automatic Recovery Profiles

Allow users to define a known-safe/default state.

Example:

```text
Default Avatar: Main Avatar
Default Scale: 1.0
Movement: Reset
Outfit: Default
Expression: Neutral
```

Crystal Relay could optionally return to this state after crashes, reconnects, or special events.

---

# 🎨 UI Ideas

## Stream Dashboard

A compact dashboard showing:

* Twitch status
* VRChat status
* OSCQuery status
* Active avatar
* Active redeems
* Current timers
* Recent viewer events
* Queued actions
* Current scale
* Active supporter effects

---

## Compact Mode

A small always-visible Crystal Relay window containing only essential controls.

Example:

```text
💎 CRYSTAL RELAY

Twitch  ●
VRChat ●
OSC     ●

Active Effects: 3

[ Pause Redeems ]
[ Emergency Reset ]
```

---

## Setup Wizard

A beginner-friendly onboarding flow.

```text
1. Connect Twitch
2. Detect VRChat
3. Detect Avatar
4. Select Parameter
5. Create Reward
6. Test Reward
7. Done
```

The goal would be getting a first interaction working with as little configuration as possible.

---

# 🧪 Developer Ideas

## Simulation Mode

Allow developers and streamers to simulate events without actually spending Channel Points, Bits, or money.

Possible simulated events:

* Channel Point redeem
* Bits
* Subscription
* Follow
* Donation
* Chat command
* Avatar change

Example:

```text
Simulate Event

Type: Bits
User: TestViewer
Amount: 500

[ Trigger ]
```

---

## Event Inspector

Show incoming events and how Crystal Relay processed them.

Example:

```text
EVENT
Twitch Bits: 500

MATCHED RULE
Tiny Avatar

ACTION
/avatar/eyeheight → 0.5

DURATION
30 seconds

RESULT
Success
```

---

## Plugin / Extension System

Long-term concept for allowing developers to extend Crystal Relay without modifying the core application.

Possible extension types:

* Trigger providers
* Actions
* Integration modules
* UI tools
* Stream platform integrations

Any plugin architecture would need strong security boundaries.

---

# 🌐 Integration Ideas

Potential future integrations could include:

* StreamElements
* Streamlabs
* Ko-fi
* OBS
* Discord
* Stream Deck
* Touch Portal
* SAMMI
* Mix It Up
* WebSocket integrations
* Local HTTP API

Integrations should remain optional so Crystal Relay does not become dependent on unnecessary external services.

---

# 📺 OBS Integration

Possible OBS actions:

* Change scene
* Toggle source
* Show overlay
* Update text
* Trigger animation
* Start/stop recording
* Trigger browser-source events

Example:

```text
Viewer Redeem
     ↓
Crystal Relay
     ├── VRChat → Giant Avatar
     └── OBS → Show "GIANT MODE" Overlay
```

---

# 🔌 Local API

Expose an optional local API so other applications can trigger Crystal Relay actions.

Example concept:

```http
POST /api/actions/trigger
```

```json
{
  "action": "avatar_scale",
  "value": 0.5,
  "duration": 30
}
```

Security requirements should include:

* Disabled by default
* Localhost-only by default
* Authentication/token support if remote access is ever allowed
* Rate limiting
* Permission controls

---

# 🏗️ Idea Priority

Ideas can roughly be categorized as:

### 🟢 Small

Features that are relatively isolated or mostly quality-of-life improvements.

### 🟡 Medium

Features involving multiple Crystal Relay systems or new UI.

### 🔴 Large

Major systems requiring significant architecture, testing, or security work.

### 🔵 Experimental

Ideas worth exploring but not necessarily suitable for the main application.

---

# ✅ Idea Evaluation

Before turning an idea into a feature, consider:

* Does it improve streamer/viewer interaction?
* Can a normal user understand how to configure it?
* Does it work safely when multiple actions happen simultaneously?
* Can Crystal Relay recover if VRChat disconnects?
* Can Crystal Relay recover if Twitch disconnects?
* What happens if the avatar changes halfway through the action?
* Can the action be cancelled?
* Can the original state be restored?
* Could the feature expose private information?
* Does it require unnecessary external services?
* Will it remain maintainable?

---

# 🤝 Contributing Ideas

Feature suggestions are welcome.

When suggesting an idea, try to include:

```text
Feature Name:

What problem does it solve?

How should it work?

Example:

What should happen if it is interrupted?

What should happen if the avatar changes?

Should it restore the previous state?

Anything else?
```

Ideas do not need to be completely designed before being suggested.

Sometimes a strange idea becomes a great feature after experimentation.

---

# 💎 Philosophy

Crystal Relay should make it possible to build increasingly creative Twitch + VRChat interactions without forcing streamers to become programmers.

The ideal workflow is:

```text
Imagine interaction
      ↓
Configure interaction
      ↓
Test interaction
      ↓
Stream
      ↓
Chaos
      ↓
Crystal Relay safely restores everything
```

More interaction.

More experimentation.

More chaos.

**Without making the setup miserable.**
