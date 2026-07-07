# Android Live Feedback App — Major Update

Date: 2026-07-06
Product: Crystal Relay Live Android
Current version: 0.1.3
Target version: 0.2.0

## Overview

Port key features from the desktop Crystal Relay Live List tool to the Android app, adapted for mobile use. The app uses Xamarin.Android-style (.NET Android) with all-programmatic UI (no AXML). The update adds bottom tab navigation, favorites/disliked, search, history, version badges, poll interval settings, and session stats — all stored via SharedPreferences.

## Tab Structure

Bottom navigation bar with three tabs:

- **Live** — filtered live user list with search, favorites, version/channel badges
- **History** — 24h history of observed live users
- **Settings** — endpoint, alerts, poll interval, session stats

## Live Tab

### Search
- EditText at top of the tab with hint "Search streamers..."
- Filters displayed user cards by display name (case-insensitive contains match)
- Filters in real-time as user types
- Search only affects the current Live tab view, not notification logic

### User Cards
Each live user renders as a card containing:

- **Favorite star** (★/☆) — tap to toggle. Filled star = favorited. Uses Unicode or Android built-in star drawable.
- **Display name** — weighted to fill remaining space
- **LIVE pill** — existing green/cyan rounded badge
- **Version pill** — `relayVersion` text in a rounded badge (e.g. `3.1.9-beta5`). Pill is hidden if version is empty.
- **Channel pill** — `buildChannel` text in a rounded badge (e.g. `beta`, `stable`, `test`). Pill is hidden if channel is empty.
- **Twitch URL** — shown below the name row
- **Last heartbeat time** — shown below URL

### Tap Behavior
- Tap anywhere on a user card → constructs `twitch://stream/<channel>` from the Twitch URL and opens it via `StartActivity(Intent.ActionView)`.
- Falls back to `https://www.twitch.tv/<channel>` if no Twitch app is installed.
- No in-app WebView stream viewer.

### Favorites Filter
- Toggle button above the user list labeled "Favorites only"
- When active, hides all users not marked as favorite
- When inactive, shows all users (respecting search filter)

### Disliked Behavior
- Long-press on the favorite star toggles disliked state
- Disliked users are shown with muted styling (dimmed text color)
- Disliked users do not trigger notification alerts (already handled by notification diffing)
- Disliked users are filtered out by default, but appear if "Show disliked" toggle is active or if they match search

### Empty States
- "No live users right now" when list is empty
- "No favorites live right now" when favorites filter is on and empty

## History Tab

### Data Model
- Stores entries with: display name, twitch URL, relay version, build channel, first seen time, last seen time
- Pruned to 24h window on every load
- Persisted in SharedPreferences as a JSON array

### Display
- Sorted by last-seen time descending
- Each card shows:
  - Display name
  - Version + channel pills
  - "First seen: <local time>"
  - "Last seen: <local time>"
- Tap → same Twitch app opening behavior as Live tab
- Empty state: "No history yet. Users you observe will appear here."

## Settings Tab

### Controls
- **Endpoint URL** — EditText (same as current, pre-filled with existing value)
- **Phone alerts** — Switch toggle (same as current)
- **Poll interval** — Radio button group:
  - Normal (~15 minutes) — default
  - Fast (~30 seconds) — only while app is visible
- **Save endpoint** button — saves and resets snapshot
- **Refresh now** button — manual refresh

### Session Stats
- Session started at (time)
- Session duration (elapsed since app opened)
- Peak live count (highest number of live users seen this session)
- Unique streamers seen (count of unique Twitch URLs this session)

Stats reset when the app process is killed (in-memory only).

### Background Refresh Info
- Text: "Background checks are roughly every N minutes. Android may delay them in battery saver or deep sleep."
- N reflects the configured poll interval

## Data Persistence

All using Android `ISharedPreferences` (same as current `LiveSettings` pattern):

| Data | Key | Format |
|------|-----|--------|
| Favorites | `favorite_keys` | JSON array of Twitch URL keys |
| Disliked | `disliked_keys` | JSON array of Twitch URL keys |
| History | `history_entries` | JSON array of entry objects |
| Poll interval | `poll_interval` | `"normal"` or `"fast"` |

Favorites and disliked use the same key format as the current snapshot system: Twitch URL `"https://www.twitch.tv/<channel>"`.

## Fast Poll Behavior

- Fast interval (~30s) only applies while the app is visible (between `OnResume` and `OnPause`)
- On `OnPause`, if fast was set, the in-app polling timer stops and the Android AlarmManager schedule is re-created at the normal interval
- On `OnResume`, if fast is configured, the in-app timer switches to fast and the alarm is re-scheduled at fast
- The AlarmManager-based background check always uses whatever interval is configured

## Version Badge Integration

The heartbeat already sends `relayVersion` and `buildChannel` from the main app. Once we apply the `AppVersion` → `GetAppUpdateVersion()` change in `MainWindowViewModel.cs:17771`, the version string will include the beta label (e.g. `"3.1.9-beta5"`). The Android app already parses both fields and will display them as styled pills.

## Implementation Plan

### Files to modify (Android app)

All changes are in the existing Android project at:
`E:\!!!Program to work on\Proper Crystal Relay\tools\private\crystal-relay-live-android\`

1. **LiveMonitor.cs** — Add:
   - `LiveFavoritesStore` class (SharedPreference-backed JSON array for favorite keys)
   - `LiveDislikedStore` class (same pattern for disliked keys)
   - `LiveHistoryStore` class (in-memory with SharedPreference persistence, 24h prune)
   - `LiveStatsTracker` class (in-memory session stats)
   - Update `LiveMonitorClient.CheckAsync()` to accept and return favorites/disliked/history data
   - Add poll interval setting read/write

2. **MainActivity.cs** — Major restructure:
   - Add bottom tab bar (3 buttons styled as tabs)
   - Restructure UI into tab-switching layout
   - **Live tab**: search bar, favorites toggle, user list with badges and favorite/dislike toggles
   - **History tab**: history list
   - **Settings tab**: poll interval, stats display
   - Tap handler opens Twitch app via intent
   - Fast poll on resume/pause

3. **CrystalRelayLiveAndroid.csproj** — Bump version to `0.2.0`

### Files to modify (main app)

1. **MainWindowViewModel.cs:17771** — Swap `AppVersion` for `GetAppUpdateVersion()` to include beta label in heartbeat

### No changes needed

- Cloudflare worker (already stores/returns `relayVersion` and `buildChannel` as-is)
- Desktop Live List tool (already displays both fields)
