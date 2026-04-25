Crystal Relay - Twitch to OSC for VRChat
========================================

What Crystal Relay Is
---------------------
Crystal Relay is a Windows desktop app that connects Twitch events to VRChat through OSC and OSCQuery.
It is built to keep setup cleaner, wording friendlier, and stream control easier to understand.

Main Features
-------------
- Twitch trigger types:
  - Channel Points
  - Bits
  - Subscriptions
- OSC action types:
  - Avatar Parameter
  - Avatar Change
  - Player Movement
- Rule areas:
  - Avatar Sets
  - Avatar Change
  - Movement Redeems
  - Bits + Subs Overrides
- Managed Twitch reward support with reward syncing, cleanup recovery, and cooldown-aware status updates
- Pause Redeems button for temporarily stopping Twitch redeems during serious stream moments
- Streaming Test Mode for checking reward behavior before going live
- Built-in Twitch Chatbox window with optional VRChat chat relay
- VRChat avatar login with 2FA, avatar cache, and OSC parameter cache
- About page creator and playtester cards with live markers
- Multiple built-in themes across the whole app

Quick Start
-----------
1. Launch Crystal Relay.
2. Open Settings.
3. Connect your Broadcaster Twitch account.
4. Optional: connect a Bot account.
5. Optional but recommended: connect VRChat Avatar Access so avatar lists and OSC parameter tools are easier to use.
6. Return to Home.
7. Pick the rule area you want to work in, add a redeem, and use Test Redeem if you want to try it before going live.
8. Turn on Streaming Test Mode when you want managed Twitch rewards visible for testing.

How The Rule Areas Work
-----------------------
- Avatar Sets group redeems by avatar. Those redeems only work while that avatar is active.
- Avatar Change is for redeems that switch you to another avatar, then optionally return you after the timer ends.
- Movement Redeems are global and are not tied to one avatar.
- Bits + Subs Overrides are global paid overrides and can ignore avatar matching.
- Disable Pairing lets sibling redeems inside the same avatar set temporarily turn each other off while one is active.

Managed Twitch Channel Point Rewards
------------------------------------
Crystal Relay can create and manage Twitch channel point rewards for supported broadcaster accounts.

- Managed rewards only turn on while:
  - you are live, or
  - Streaming Test Mode is enabled
- On app shutdown, Crystal Relay tries to disable managed rewards before exit.
- If shutdown is interrupted, Crystal Relay runs a recovery cleanup check on next launch.
- Twitch channel point reward management is only available for affiliate or partner broadcaster accounts.
- If a broadcaster account cannot use Twitch custom rewards, Crystal Relay now keeps running and skips managed reward syncing instead of crashing.

Pause Redeems
-------------
- Pause Redeems is a quick stream-safe stop button.
- While paused, Crystal Relay turns managed Twitch redeems off and ignores Twitch-triggered actions until you resume.
- The pause state is remembered between launches.

Streaming Test Mode
-------------------
- Streaming Test Mode simulates stream-ready managed reward behavior for testing.
- It does not force every avatar set active at once.
- It uses the avatar set you are actively testing instead of making every avatar profile live at the same time.
- It resets to OFF each new app launch.

Twitch Chatbox
--------------
- Open it with the Twitch Chatbox button.
- Supports font choices, text size, overlay mode, always-on-top, and 12-hour or 24-hour timestamps.
- Can optionally send Twitch chat into VRChat with adjustable pacing.
- Uses the same theme family as the main app.

VRChat Avatar Access
--------------------
- Used for avatar lists and avatar-selection tools.
- Supports username/password plus 2FA.
- Avatar lists and OSC parameter caches are stored locally for faster setup.
- Refresh Avatars when you upload new avatars.
- Refresh OSC Parameters when you want the latest parameter list for the avatar you are wearing.

About Page
----------
- Shows the creator card and playtester cards inside the app.
- Live markers can update while Crystal Relay stays open.
- Profile cards can still load more reliably even before Twitch is connected.

Themes
------
Current built-in themes include:

- Void Crystal
- Dream Scape
- Bubblegum
- Cosmic Puppy Girl
- Peaches & Cream
- Moon Bunny Wink
- Dread Night Bar
- Baked

Important Folder Location
-------------------------
Crystal Relay local data is stored in:

C:\Users\<YourUser>\AppData\Local\CrystalRelay

This folder includes:
- secure app data
- runtime config
- avatar and OSC caches
- crash logs
- save-transfer files

Use Open Crystal Relay Folder inside the app to jump there quickly.

Troubleshooting
---------------
- A managed redeem did not appear on Twitch:
  - You must be live, or turn on Streaming Test Mode.
  - Avatar Set redeems only appear for the active avatar context Crystal Relay is using.
  - The broadcaster account must support Twitch channel point rewards.
- A redeem did not fire:
  - The redeem must be enabled.
  - The trigger type must match.
  - The Twitch reward name must match exactly.
  - Check whether Pause Redeems is turned on.
- Test Redeem did not work:
  - VRChat must be open.
  - OSC must be enabled in VRChat.
  - Use Force Refresh in OSC Status and test again.
- Twitch Chatbox is not receiving messages:
  - Reconnect the broadcaster account.
- A crash happened:
  - Check C:\Users\<YourUser>\AppData\Local\CrystalRelay\CrashLogs

Versioning
----------
Crystal Relay uses major.minor.patch with decimal rollovers per segment.

Examples:
- 1.0.9 -> 1.1.0
- 1.9.9 -> 2.0.0
