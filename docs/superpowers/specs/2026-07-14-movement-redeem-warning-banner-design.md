# Movement Redeem Warning Banner Design

## Summary
Add a yellow attention banner at the top of the Movement Redeems popup manager window warning users that OSC movement redeems may not work as intended across VR and desktop.

## Design Decisions
- **Location**: Popup manager window (`MovementRedeemsManagerWindow.xaml`) only, not the main window rule tab.
- **Placement**: Above the toolbar (search + Add Rule / Enable All / Disable All buttons), visible immediately on open.
- **Style**: Yellow attention banner, matching the existing pattern in `MainWindow.xaml:6859-6878` — `AttentionBrush` background, `AttentionBorderBrush` border, bold heading, body text below.
- **Approach**: Static always-visible banner (Approach A). No ViewModel changes needed. No dismiss button.
- **Text**:
  - **Heading**: "Movement Redeem Notice"
  - **Body**: "OSC movement redeems may not work as intended for all users. Some movement directions work in VR but not on desktop, and movement inputs or counter-inputs from redeems may not always produce the expected results."

## Implementation
Files to modify:
- `MovementRedeemsManagerWindow.xaml` — add the banner XAML directly above the toolbar `<Border>`.
- ViewModel: no changes needed (static banner, always visible).

## Localization
The banner text should use `{loc:Translate ...}` bindings to support localization. Add new en-US source keys in `Resources\Localization\en-US.json`:
- `"Movement Redeem Notice"` (heading)
- `"OSC movement redeems may not work as intended for all users. Some movement directions work in VR but not on desktop, and movement inputs or counter-inputs from redeems may not always produce the expected results."` (body)

If the `.extra.json` pattern is used for these keys, add them to `Resources\Localization\en-US.extra.json` instead. Then add matching keys to all other locale files (`de-DE`, `es-ES`, `fr-FR`, `it-IT`, `ja-JP`, `ko-KR`, `pl-PL`, `pt-BR`, `ru-RU`, `sv-SE`, `th-TH`, `zh-CN`, `zh-TW`).

Run the localization audit after adding keys.

No new resources, converters, or controls required.
