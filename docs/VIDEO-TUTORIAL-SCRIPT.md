# Crystal Relay Video Tutorial Script

Use this as a read-aloud script while recording a visual walkthrough. The script is written for a first-time viewer who needs to understand what Crystal Relay does, how to connect it, and how to test a redeem safely.

## Recording Safety Checklist

- Use a test Twitch reward or test scene if possible.
- Do not show passwords, OAuth approval details, VRChat 2FA codes, browser cookies, or private tokens.
- If a login page opens, pause recording or blur the screen until you are back inside Crystal Relay.
- If your VRChat avatar list shows private avatar names you do not want public, blur that area or use a safe test avatar.
- If you show GitHub, use the public release page only.

## Tutorial Goal

By the end of the video, viewers should understand:

- Crystal Relay connects Twitch events to VRChat through OSC and OSCQuery.
- Twitch Channel Points, Bits, Subs, and chat commands can trigger VRChat actions.
- Actions can change avatar parameters, switch avatars, or move the player.
- Rules can be tested before the Twitch reward or command is fully created.
- The Twitch Chatbox, themes, and update checks are optional quality-of-life tools.

---

## 0:00 - Intro

**Show on screen:** Crystal Relay main window.

**Say:**

Welcome. In this video, I am going to show how Crystal Relay works.

Crystal Relay is a Windows app that connects Twitch to VRChat through OSC and OSCQuery. That means channel point redeems, Bits, Subs, and chat commands can trigger avatar parameters, avatar swaps, movement controls, and other stream effects inside VRChat.

This tutorial will cover the basic setup, how rules are organized, how to test a redeem safely, and where to find the extra tools like the Twitch Chatbox and themes.

---

## 0:45 - Download and Launch

**Show on screen:** GitHub Releases page, then the extracted release folder.

**Say:**

To get Crystal Relay, go to the public GitHub release page and download the latest Windows release zip.

After downloading, extract the zip, open the folder, and launch the versioned Crystal Relay executable.

If Windows shows a first-run warning, that usually means the app is new and not widely recognized yet. Only continue if you downloaded it from the official public release page.

---

## 1:30 - Main Window Overview

**Show on screen:** Home section and top buttons.

**Say:**

This is the main Crystal Relay window.

The app is built around a few main areas. The Home area shows connection status and the main control buttons. Settings is where Twitch, VRChat, theme, language, and chatbox settings live. The rule sections are where you create the Twitch triggers that will control VRChat.

At the top, you can also open the Twitch Chatbox, toggle test tools, and view app information.

---

## 2:15 - Twitch Setup

**Show on screen:** Settings, Twitch account area. Pause or blur any browser login.

**Say:**

First, connect your Twitch broadcaster account.

The broadcaster account is the account that owns the channel point rewards. This is required if you want Crystal Relay to listen for channel point redeems, Bits, Subs, and chat events on your channel.

There is also an optional bot account. The bot account is only needed if you want Crystal Relay to send automatic chat messages, such as paid override info messages. If you do not need bot chat messages, you can leave the bot account disconnected.

When a browser login or Twitch device code appears, complete the login privately, then return to Crystal Relay.

---

## 3:15 - VRChat and OSC Setup

**Show on screen:** VRChat settings, avatar access, OSC status.

**Say:**

Next is VRChat setup.

Crystal Relay talks to VRChat through OSC and OSCQuery. OSC is what sends the actual parameter or movement command. OSCQuery helps Crystal Relay discover avatar parameters and connection details.

For the best experience, connect VRChat avatar access. This lets the app load avatar names and parameter information, which makes rule setup much easier.

After uploading or changing avatars, use Refresh Avatar List. When you need the newest parameters for the avatar you are wearing, use Refresh OSC Parameters.

If OSC is not working, make sure OSC is enabled in VRChat and that VRChat is running while you test.

---

## 4:30 - How Rules Are Organized

**Show on screen:** Rule areas in the app.

**Say:**

Crystal Relay has a few rule areas.

Avatar Sets are rules tied to the avatar you are currently using. These are useful when one avatar has special toggles or parameters that another avatar does not have.

Avatar Change rules switch you into another avatar. You can set them to return after a timer, or use an active time of zero for a permanent swap.

Movement Redeems trigger movement controls, like moving forward, backward, left, right, or spinning.

Bits and Subs Overrides are global paid triggers. These can temporarily override normal redeems and can use priority behavior, so higher paid triggers can interrupt or queue above lower ones.

---

## 5:45 - Create a Basic Redeem Rule

**Show on screen:** Create or select a channel point rule.

**Say:**

Now I will set up a basic rule.

Start by selecting the rule area you want. For a simple avatar toggle, use an Avatar Set rule and choose Avatar Parameter as the action.

The trigger side is where Twitch matching happens. For a live channel point redeem, you normally fill in the reward name or connect it to a managed reward. You can also enable a chat command if you want chat to trigger it.

The action side is what Crystal Relay sends to VRChat. For an avatar parameter, choose the parameter name, type, value, reset behavior, and active time.

The important idea is this: Twitch decides when the rule triggers, and the action settings decide what happens in VRChat.

---

## 7:15 - Test Redeem Without a Finished Reward

**Show on screen:** Rule with action configured, blank reward name if desired, then click Test Redeem.

**Say:**

One useful feature is Test Redeem.

You can test a selected rule even before you finish the Twitch reward name, reward ID, or chat command. That makes setup faster because you can confirm the VRChat action works first, then create or connect the real Twitch trigger later.

For the manual test to work, the action itself still needs to be valid. For example, an avatar parameter rule needs a real parameter path. An avatar change rule needs a target avatar. Avatar roulette needs a pool. Movement needs a supported direction.

If the action setup is valid, pressing Test Redeem should send the action locally.

---

## 8:30 - Managed Rewards and Test Mode

**Show on screen:** Managed reward settings and Streaming Test Mode.

**Say:**

Crystal Relay can manage Twitch custom rewards for you.

Managed rewards can turn on while you are live, or while Streaming Test Mode is enabled. This lets you check how rewards will behave before your stream starts.

Crystal Relay also tries to clean up managed rewards when it shuts down. If the app or computer closes unexpectedly, it can run recovery cleanup the next time it opens.

Reward availability can change based on cooldowns, avatar matching, disable pairing, and Bits or Subs override rules.

---

## 9:45 - Avatar Change Redeems

**Show on screen:** Avatar Change rule settings.

**Say:**

Avatar Change rules are for swapping into another avatar.

If you set an active time, Crystal Relay can change to the target avatar and then return after that timer ends.

If you set the active time to zero, the avatar change is treated as permanent. The new avatar becomes the return avatar, which lets other avatar change redeems work cleanly from that new state.

This is useful if viewers can choose between avatars and you want each change to become the new normal until another change happens.

---

## 10:45 - Bits and Subs Overrides

**Show on screen:** Bits + Subs override section.

**Say:**

Bits and Subs Overrides are paid global rules.

These are designed for stream events that should take priority over normal channel point redeems. For example, a high-bit trigger could temporarily block avatar change redeems while it runs.

You can set separate timing behavior for Bits and Subs, including amount-based time and maximum accumulated time. This lets paid triggers scale without letting one override run forever.

If you connect a bot account, Crystal Relay can also post a compact chat message explaining what paid override ran and how much time was added.

---

## 12:00 - Twitch Chatbox

**Show on screen:** Twitch Chatbox window and settings.

**Say:**

Crystal Relay also includes a Twitch Chatbox.

The chatbox can show Twitch chat in its own themed window. You can adjust the font, text size, card opacity, timestamps, overlay mode, always-on-top behavior, and optional viewer message sound.

There is also an option to relay Twitch chat into VRChat chatbox messages. If you use that, set a delay between posts so messages are paced safely and do not spam too fast.

---

## 13:00 - Themes and Customization

**Show on screen:** Theme dropdown, Treetender's Arm, Custom theme editor.

**Say:**

Crystal Relay has multiple built-in themes, including Void Crystal, Dream Scape, Bubblegum, Cosmic Puppy Girl, Peaches and Cream, Moon Bunny Wink, Dread Night Bar, Baked, MainFrame, Trash Kitty, and Treetender's Arm.

There is also a Custom theme slot. With Custom, you can edit the app colors, body font, heading font, and optionally choose a main-window background image.

The custom background image is copied into Crystal Relay's app data folder, so moving the original file later will not break the theme.

---

## 14:00 - Updates, Local Data, and Logs

**Show on screen:** About page, app folder button if available, GitHub release page.

**Say:**

Crystal Relay can check the public GitHub release page on startup. If a newer release is available, it shows a themed popup with the current version and latest version.

Choosing update opens the GitHub release page in your browser. Crystal Relay does not auto-download or self-patch in the background.

Local app data is stored under AppData Local, inside the CrystalRelay folder. That is where runtime settings, cache files, save-transfer data, theme assets, and crash logs live.

If something goes wrong, crash logs are stored in the CrashLogs folder.

---

## 15:00 - Wrap-Up

**Show on screen:** Main window with a working rule selected.

**Say:**

That is the basic workflow for Crystal Relay.

Connect Twitch, connect VRChat if you want avatar and parameter tools, create a rule, test the action locally, then connect it to a real Twitch reward or chat command when you are ready.

The main thing to remember is that rules have two parts: the Twitch trigger and the VRChat action. If the action works in Test Redeem, you can build the Twitch side with more confidence.

You can download the latest version from the public GitHub release page.

Thanks for watching.

---

## Optional Short Version for a 60-Second Clip

**Say:**

Crystal Relay is a Windows app that connects Twitch to VRChat through OSC and OSCQuery.

You can use channel points, Bits, Subs, or chat commands to trigger VRChat avatar parameters, avatar changes, movement controls, and paid override effects.

The setup flow is simple: connect your broadcaster Twitch account, enable OSC in VRChat, connect VRChat avatar access if you want avatar and parameter lists, then create a rule.

Rules have two parts: the Twitch trigger and the VRChat action. You can test the action with Test Redeem before the reward name or chat command is finished, which makes setup much faster.

Crystal Relay also includes managed Twitch rewards, a themed Twitch Chatbox, built-in themes, a Custom theme editor, startup update checks, and local crash logs.

Download it from the Crystal Relay public GitHub releases page.
