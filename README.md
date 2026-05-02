# Crystal Relay

**Windows desktop Twitch-to-VRChat control through OSC and OSCQuery.**

[![Platform](https://img.shields.io/badge/platform-Windows-0078D4?style=for-the-badge)](#build-and-run-from-source)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge)](#build-and-run-from-source)
[![License](https://img.shields.io/badge/license-GPL--3.0--or--later-2E8B57?style=for-the-badge)](LICENSE)
[![Twitch](https://img.shields.io/badge/Twitch-EventSub%20%2B%20Helix-9146FF?style=for-the-badge)](#network-and-privacy-basics)
[![VRChat](https://img.shields.io/badge/VRChat-OSC%20%2B%20OSCQuery-1DA1F2?style=for-the-badge)](#vrchat-and-osc)

![Crystal Relay main window](docs/images/crystal-relay-main-window-v2-8-6.png)

Crystal Relay connects Twitch events to VRChat through local OSC and OSCQuery control. It is built for streamers who want Channel Point rewards, chat commands, bits, subs, follows, avatar parameters, avatar changes, movement redeems, and chatbox relay tools without turning setup into a maze.

- [Download packaged releases](https://github.com/seluvia/crystal-relay-public/releases)
- [Watch the tutorial video](https://github.com/seluvia/crystal-relay-public/releases/download/v2.9.0/CrystalRelay-Tutorial-v2.9.0.mp4)
- [Build and run from source](#build-and-run-from-source)

## Contents

| Start Here | Stream Tools | Technical Info | Project Info |
| --- | --- | --- | --- |
| [Quick Start](#quick-start) | [Managed Twitch Rewards](#managed-twitch-rewards) | [Network and Privacy Basics](#network-and-privacy-basics) | [Open Source and AI Transparency](#open-source-and-ai-transparency) |
| [What Crystal Relay Handles](#what-crystal-relay-handles) | [Universal Triggers](#universal-triggers) | [Local Data and Crash Logs](#local-data-and-crash-logs) | [License](#license) |
| [Trigger Areas and Actions](#trigger-areas-and-actions) | [Twitch Chatbox](#twitch-chatbox) | [Build and Run From Source](#build-and-run-from-source) | [Versioning](#versioning) |
| [Main Features](#main-features) | [VRChat and OSC](#vrchat-and-osc) | [Releases](#releases) | |

## Quick Start

1. Launch **Crystal Relay**.
2. Open **Settings**.
3. Connect your **Broadcaster** Twitch account.
4. Optional: connect a **Bot** Twitch account for bot chat messages.
5. Optional but recommended: connect **VRChat Avatar Access** so avatar lists and OSC parameter tools are available.
6. Return to **Home** and create the redeem rules you want.
7. Use **Test Redeem** or **Streaming Test Mode** before going live so you can confirm behavior safely.

## What Crystal Relay Handles

| Category | Supported Areas |
| --- | --- |
| **Twitch triggers** | Channel Points, Chat Commands, Bits, Subscriptions, Gift Subs, Follows |
| **OSC actions** | Avatar Parameters, Avatar Changes, Player Movement |
| **Rule areas** | Avatar Sets, Avatar Change, Movement Redeems, Bits + Subs Overrides, Universal Triggers |
| **Managed rewards** | Twitch reward creation, adoption, syncing, cleanup recovery, cooldown-aware state, per-redeem colors |
| **Chat tools** | Built-in Twitch Chatbox with optional VRChat chat relay |
| **VRChat tools** | VRChat login with 2FA, avatar cache, OSC parameter cache, OSCQuery discovery |
| **App polish** | Built-in themes, localization, About-page live cards, crash logging |

## Main Features

- Managed Twitch reward syncing with cleanup and recovery handling
- Pause Redeems for quickly disabling Twitch-triggered actions during stream moments
- Streaming Test Mode for checking managed reward behavior before going live
- Per-redeem ready and cooldown colors for managed Channel Point rewards
- Priority-based Bits + Subs override queueing with timed stacking controls
- Optional bot info messages for supporter overrides
- Avatar-aware redeem libraries for avatar-specific and global behaviors
- Built-in Twitch Chatbox with theme-aware settings and optional VRChat relay
- Theme support across the main window, dialogs, and chatbox
- Built-in language support for English, Spanish, Japanese, German, French, Portuguese (Brazil), Swedish, Italian, Simplified Chinese, Traditional Chinese, Korean, Russian, Polish, and Thai

## Trigger Areas and Actions

### Rule Areas

| Area | What It Does |
| --- | --- |
| **Avatar Sets** | Groups redeems by avatar so they only stay live while that avatar is active. |
| **Avatar Change** | Switches to another avatar, then optionally returns after a timer. |
| **Movement Redeems** | Sends timed VRChat movement inputs that are not tied to one avatar. |
| **Bits + Subs Overrides** | Runs global paid override rules that can preempt normal redeems. |
| **Universal Triggers** | Imports or creates general Twitch interactions for commands, rewards, bits, subs, gift subs, and follows. |

### Action Types

| Action Type | Result |
| --- | --- |
| **Avatar Parameter** | Sends bool, int, or float parameter values into VRChat. |
| **Avatar Change** | Swaps to another avatar temporarily or permanently. |
| **Player Movement** | Holds a movement input for the configured active time. |

## Managed Twitch Rewards

Crystal Relay can create and manage Twitch custom rewards for supported broadcaster accounts.

- Managed rewards turn on while you are live or while **Streaming Test Mode** is enabled.
- On shutdown, Crystal Relay tries to disable managed rewards cleanly.
- If the previous session ended badly, Crystal Relay runs a recovery cleanup pass on the next launch.
- Reward availability follows cooldowns, avatar matching, disable-pairing, and Bits/Subs override suppression rules.
- Twitch custom reward management requires a broadcaster account that supports custom rewards.

## Universal Triggers

Universal Triggers are for broader Twitch interaction setups that should live outside avatar-specific redeem lists.

- Import **Fooma Twitch Interaction** JSON configs for assist-style OSC actions.
- Supports **Chat Commands**, **Channel Point Rewards**, **Bits**, **Subscriptions**, **Gift Subscriptions**, and **Follows**.
- Fuses matching `!command` entries into their matching Channel Point reward so one card can handle both trigger paths.
- Sends direct OSC action lists, with optional random action selection and queued activations.
- Keeps Universal Channel Point rewards outside Avatar Sets, so they are not tied to a configured avatar profile.
- For Universal rewards that send `/avatar/parameters/...` actions, Crystal Relay checks the local OSC JSON for the avatar VRChat is currently running. If at least one needed parameter path is found, the reward can be made available on Twitch; if the path is missing or the JSON is not ready, the reward stays hidden/off instead of being offered.
- Universal actions that do not target avatar-parameter paths do not need this parameter gate.

Need the Fooma Twitch Interaction system? Get it here:

[Fooma Twitch Interaction on Gumroad](https://foomaring.gumroad.com/l/lmrjbl)

## Twitch Chatbox

Open the **Twitch Chatbox** from inside Crystal Relay to add a compact, theme-aware chat display for stream use.

- Supports font choice, text size, overlay mode, always-on-top, and 12-hour or 24-hour timestamps.
- Can optionally relay Twitch chat into VRChat with adjustable pacing.
- Supports text-only and emote-rendered chat display paths.
- Uses the same theme family as the main app.

## VRChat and OSC

Crystal Relay supports VRChat username/password login with 2FA and uses local avatar and OSC parameter caches to keep setup faster between launches.

- Use **Refresh Avatar List** after uploading new avatars.
- Use **Refresh OSC Parameters** when you want the latest parameters for the avatar you are wearing.
- OSCQuery discovery helps Crystal Relay find the running VRChat client.
- OSC sends avatar parameters, avatar changes, movement inputs, and chatbox messages to VRChat locally.

## Themes and Localization

Current built-in themes:

- Void Crystal
- Treetender's Arm
- Carrot Patch
- Dream Scape
- Bubblegum
- Cosmic Puppy Girl
- Peaches & Cream
- Moon Bunny Wink
- Dread Night Bar
- Baked
- MainFrame
- Trash Kitty

Built-in language support includes English, Spanish, Japanese, German, French, Portuguese (Brazil), Swedish, Italian, Simplified Chinese, Traditional Chinese, Korean, Russian, Polish, and Thai.

## Network and Privacy Basics

Crystal Relay uses standard network connections only for the services needed to run.

| Service | Connection Use |
| --- | --- |
| **Twitch** | Outbound HTTPS for login, Helix API calls, reward management, emote data, and optional bot chat sends. Live Twitch events are received through EventSub over WebSocket. |
| **VRChat** | Outbound HTTPS for login, 2FA, logout, and avatar list refreshes. Runtime avatar control uses local OSC and OSCQuery traffic between Crystal Relay and VRChat. |
| **GitHub** | Outbound HTTPS to the public Crystal Relay releases API for update checks. |
| **Cloudflare** | Outbound HTTPS to Crystal Relay Cloudflare Workers for About-page beta live-status cards and optional in-app bug reports. Bug reports are user-submitted, logs are opt-in and sanitized before sending, and Twitch tokens, VRChat auth cookies, passwords, and private app data must never be sent. |
| **Local machine** | OSC and OSCQuery use local UDP/TCP ports for discovery, avatar parameters, movement inputs, avatar changes, and chatbox messages. This is local app-to-VRChat communication, not a public remote-control server. |

Crystal Relay does **not** publish Twitch tokens, VRChat auth cookies, private IP addresses, local usernames, downloaded VRChat avatar files, or runtime app data into the README or public repository.

Login/session secrets are stored locally through **Windows Credential Manager**, and runtime caches stay in the user's local app data folder.

## Local Data and Crash Logs

Crystal Relay stores local data in:

```text
C:\Users\<YourUser>\AppData\Local\CrystalRelay
```

This folder includes:

- secure app data
- runtime config
- avatar and OSC caches
- crash logs
- save-transfer files

Crash logs are written under:

```text
C:\Users\<YourUser>\AppData\Local\CrystalRelay\CrashLogs
```

Use **Open Crystal Relay Folder** inside the app to jump there quickly.

## Build and Run From Source

Prerequisites:

- Windows
- .NET 10 SDK with Windows desktop support

Build the solution:

```powershell
dotnet build .\VrcTwitchOscBridge.slnx
```

Run from source:

```powershell
powershell -ExecutionPolicy Bypass -File .\Run-Crystal-Relay-Source.ps1
```

Create a test package:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Crystal-Relay-Test.ps1
```

Create a release package:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Crystal-Relay-Release.ps1
```

## Releases

Packaged Windows builds are published on the public GitHub Releases page:

[Crystal Relay Releases](https://github.com/seluvia/crystal-relay-public/releases)

## Open Source and AI Transparency

Crystal Relay was made with help from AI tools, alongside hands-on testing, direction, design choices, and project care from its creator.

That is shared openly because there is nothing to hide here. This project is meant to be transparent, open source, and useful for the VRChat streaming community.

Programmers are welcome to study it, improve it properly, or make their own versions so better Twitch-to-VRChat tools can exist for more streamers.

## License

Crystal Relay is licensed under the **GNU General Public License v3.0 or later** (`GPL-3.0-or-later`).

You are welcome to use, study, share, and modify the source code under the GPL. If you distribute a modified version, you must keep it under the same GPL terms and provide the corresponding source code.

See [LICENSE](LICENSE) for the full license text.

The license protects the source code. The Crystal Relay name, project identity, and branding should not be used to imply that an unofficial fork or modified build is the official Crystal Relay release.

## Versioning

Crystal Relay uses `major.minor.patch` with decimal rollovers per segment.

| Example | Meaning |
| --- | --- |
| `1.0.9 -> 1.1.0` | Patch segment rolls into the next minor version. |
| `1.9.9 -> 2.0.0` | Minor segment rolls into the next major version. |
