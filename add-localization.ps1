# Universal Triggers i18n update script
# Text-based approach: removes retired key lines and appends new keys before closing }

$localeDir = "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\Resources\Localization"
$files = Get-ChildItem -LiteralPath $localeDir -Filter "*.extra.json"

# 88 new keys (key -> English source value)
$newKeysList = @(
    @{ "Universal Triggers Subtitle" = "Run direct OSC actions from Twitch events, across any avatar that has the params." },
    @{ "Universal Triggers New Trigger" = "+ New trigger" },
    @{ "Universal Triggers Import Fooma" = "Import Fooma" },
    @{ "Universal Triggers Delete All" = "Delete all" },
    @{ "Universal Triggers Filter All" = "All ({0})" },
    @{ "Universal Triggers Filter Ready" = "Ready ({0})" },
    @{ "Universal Triggers Filter Warnings" = "Warnings ({0})" },
    @{ "Universal Triggers Filter Fooma" = "From Fooma ({0})" },
    @{ "Universal Triggers Search Placeholder" = "Search triggers or params..." },
    @{ "Universal Triggers Card Action Summary" = "{0} action(s), {1}s total . {2}" },
    @{ "Universal Triggers Ready" = "[CHECK] Ready for current avatar" },
    @{ "Universal Triggers Warn Direct Osc" = "[WARN] Direct OSC paths" },
    @{ "Universal Triggers Warn Not Avatar Bound" = "[WARN] Not avatar-bound" },
    @{ "Universal Triggers Danger Missing Param" = "[DANGER] {0} param missing" },
    @{ "Universal Triggers Danger No Actions" = "[DANGER] No complete actions" },
    @{ "Universal Triggers Info Needs Setup" = "Needs setup" },
    @{ "Universal Triggers Info Moderators" = "Moderators+" },
    @{ "Universal Triggers Info Fused With Command" = "+ !{0} command" },
    @{ "Universal Triggers Info From Fooma" = "from Fooma" },
    @{ "Universal Triggers Editor Avatar Readiness" = "Avatar Readiness" },
    @{ "Universal Triggers Editor Readiness For Avatar" = "For the current avatar ({0}):" },
    @{ "Universal Triggers Editor Readiness Param Found" = "[CHECK] param found in current avatar" },
    @{ "Universal Triggers Editor Readiness Param Missing" = "[DANGER] missing from current avatar OSC JSON, OSC send will no-op" },
    @{ "Universal Triggers Editor Readiness Reward Shown" = "N actions target this param . reward shows on Twitch" },
    @{ "Universal Triggers Editor Readiness Reward Hidden" = "reward hidden on Twitch until avatar has the param" },
    @{ "Universal Triggers Editor Section Trigger Settings" = "Trigger settings" },
    @{ "Universal Triggers Editor Section Twitch Reward" = "Twitch reward" },
    @{ "Universal Triggers Editor Section Osc Actions" = "OSC actions" },
    @{ "Universal Triggers Editor Footer Test" = "Test now" },
    @{ "Universal Triggers Editor Footer Duplicate" = "Duplicate" },
    @{ "Universal Triggers Editor Footer Delete" = "Delete" },
    @{ "Universal Triggers Editor Footer Save" = "Save" },
    @{ "Universal Triggers Wizard Title" = "Create a new trigger" },
    @{ "Universal Triggers Wizard Step N of 4" = "Step {0} of 4" },
    @{ "Universal Triggers Wizard Cancel" = "Cancel" },
    @{ "Universal Triggers Wizard Back" = "Back" },
    @{ "Universal Triggers Wizard Next" = "Next" },
    @{ "Universal Triggers Wizard Step 1 Title" = "What should trigger this?" },
    @{ "Universal Triggers Wizard Step 1 Hint" = "Pick one of the six Twitch events to drive this trigger." },
    @{ "Universal Triggers Wizard Event Chat Command" = "Chat Command" },
    @{ "Universal Triggers Wizard Event Chat Command Hint" = "e.g. !wave" },
    @{ "Universal Triggers Wizard Event Channel Point" = "Channel Point Reward" },
    @{ "Universal Triggers Wizard Event Channel Point Hint" = "Twitch redeem" },
    @{ "Universal Triggers Wizard Event Bits" = "Bits" },
    @{ "Universal Triggers Wizard Event Bits Hint" = "Cheering" },
    @{ "Universal Triggers Wizard Event Subscription" = "Subscription" },
    @{ "Universal Triggers Wizard Event Subscription Hint" = "New sub" },
    @{ "Universal Triggers Wizard Event Gift Sub" = "Gift Subscription" },
    @{ "Universal Triggers Wizard Event Gift Sub Hint" = "Gift sub event" },
    @{ "Universal Triggers Wizard Event Follow" = "Follow" },
    @{ "Universal Triggers Wizard Event Follow Hint" = "New follower" },
    @{ "Universal Triggers Wizard Step 2 Title" = "Configure the event" },
    @{ "Universal Triggers Wizard Step 2 Channel Point" = "Tell Crystal Relay about the reward." },
    @{ "Universal Triggers Wizard Step 2 Chat Command" = "Tell Crystal Relay which chat command and who can use it." },
    @{ "Universal Triggers Wizard Step 2 Bits" = "Set the bits range that fires this trigger." },
    @{ "Universal Triggers Wizard Step 2 Subscription" = "Set the subscription tier and month range." },
    @{ "Universal Triggers Wizard Step 2 Follow" = "Fires on every new follower." },
    @{ "Universal Triggers Wizard Step 3 Title" = "Add OSC actions" },
    @{ "Universal Triggers Wizard Step 3 Hint" = "What should this trigger send? Pick params from the current avatar or type a path." },
    @{ "Universal Triggers Wizard Step 3 Add Action" = "+ Add another action" },
    @{ "Universal Triggers Wizard Step 3 Random" = "Run a random one of these per trigger" },
    @{ "Universal Triggers Wizard Step 3 Params Available" = "Params found in current avatar: {0} . {1} of {2} used" },
    @{ "Universal Triggers Wizard Step 3 Params No Avatar" = "Load a VRChat avatar to see available params" },
    @{ "Universal Triggers Wizard Step 4 Title" = "Review and save" },
    @{ "Universal Triggers Wizard Step 4 Hint" = "Looks good? Test it now or save it." },
    @{ "Universal Triggers Wizard Step 4 Test" = "Test now" },
    @{ "Universal Triggers Wizard Step 4 Save" = "Save trigger" },
    @{ "Universal Triggers Import Title" = "Import Fooma Config" },
    @{ "Universal Triggers Import Step N of 3" = "Step {0} of 3 - {1}" },
    @{ "Universal Triggers Import Step Preview" = "Preview" },
    @{ "Universal Triggers Import Step Done" = "Done" },
    @{ "Universal Triggers Import File Summary" = "{0} commands . {1} channel rewards . {2} sub rules . {3} bits rules . {4} follow rule(s) . ({5} will be fused into rewards)" },
    @{ "Universal Triggers Import Will Create" = "This will create the following triggers:" },
    @{ "Universal Triggers Import More Truncated" = "+ {0} more ({1}) - click Expand to see all" },
    @{ "Universal Triggers Import Warn Direct Osc" = "The !{0} command uses built-in /input/* paths. That won't gate reward visibility on avatar params. It'll still work, but the warning system will mark it as not avatar-bound." },
    @{ "Universal Triggers Import After Note" = "Crystal Relay will create the rewards on Twitch (if Create or manage is on), tag them with the VRC: prefix, and link any matching commands to their reward. You can re-import the same file later to update." },
    @{ "Universal Triggers Import Confirm" = "Import {0} triggers" },
    @{ "Universal Triggers Import Done Summary" = "Imported {0} triggers ({1} command+reward pairs fused)" },
    @{ "Universal Triggers Onboarding Title" = "Welcome to Universal Triggers" },
    @{ "Universal Triggers Onboarding Body" = "Universal Triggers run direct OSC actions from Twitch events. They listen to avatar params (not avatar sets), so a reward can fire on any avatar that has the param -- private or public." },
    @{ "Universal Triggers Onboarding Import Title" = "Import Fooma Config" },
    @{ "Universal Triggers Onboarding Import Body" = "Have a Fooma Twitch Interaction JSON? Import it and you're done." },
    @{ "Universal Triggers Onboarding Import Action" = "Choose .json file" },
    @{ "Universal Triggers Onboarding Create Title" = "Create from scratch" },
    @{ "Universal Triggers Onboarding Create Body" = "A short guided setup. Pick a Twitch event, point to a param, done." },
    @{ "Universal Triggers Onboarding Create Action" = "Start wizard" },
    @{ "Universal Triggers Onboarding Help Question" = "How does the avatar param thing work?" },
    @{ "Universal Triggers Onboarding Help Body" = "Crystal Relay reads each avatar's local OSC JSON when it loads. If your trigger targets /avatar/parameters/twitch and the current avatar declares twitch as a param, the reward shows on Twitch. Switch to an avatar without twitch and the reward hides (or is deleted if you turned that on). Direct OSC paths like /input/Jump always run regardless of avatar." }
)

# Merge into dictionary
$newKeys = @{}
foreach ($h in $newKeysList) {
    foreach ($k in $h.Keys) {
        $newKeys[$k] = $h[$k]
    }
}

# 4 retired keys to remove
$retiredKeys = @(
    "Universal Trigger Setup Warning",
    "Import a Fooma config or add a universal trigger.",
    "Import a Fooma config or add a universal trigger to edit it.",
    "Add Universal Trigger"
)

function EscapeJsonValue($s) {
    # Escape special characters for JSON string values
    $s = $s -replace '\\', '\\\\'
    $s = $s -replace '"', '\"'
    $s = $s -replace "`n", '\n'
    $s = $s -replace "`r", '\r'
    $s = $s -replace "`t", '\t'
    return $s
}

$successCount = 0
$failCount = 0

foreach ($file in $files) {
    try {
        # Read file as UTF-8 text
        $content = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)

        # Remove retired keys (match lines like "  "Key": "value",")
        $lines = $content -split "`n"
        $newLines = @()
        $removedCount = 0
        $addedCount = 0

        foreach ($line in $lines) {
            $shouldRemove = $false
            foreach ($key in $retiredKeys) {
                # Match: optional whitespace, quote, key, quote, colon, optional whitespace, value, optional comma
                $escapedKey = [regex]::Escape($key)
                if ($line -match "^\s*`"$escapedKey`"\s*:\s*`"") {
                    $shouldRemove = $true
                    Write-Host "[$($file.Name)] Removed: $key" -ForegroundColor Yellow
                    $removedCount++
                    break
                }
            }
            if (-not $shouldRemove) {
                $newLines += $line
            }
        }

        # Find the line with just "  }" (closing brace with 2-space indent) and insert new keys before it
        $lastNonEmptyIdx = -1
        for ($i = $newLines.Count - 1; $i -ge 0; $i--) {
            if ($newLines[$i].Trim() -ne "") {
                $lastNonEmptyIdx = $i
                break
            }
        }

        # Find closing brace line
        $closingBraceIdx = -1
        for ($i = $newLines.Count - 1; $i -ge 0; $i--) {
            if ($newLines[$i] -match '^\s*\}\s*$') {
                $closingBraceIdx = $i
                break
            }
        }

        if ($closingBraceIdx -ge 0) {
            # Build new key lines
            $newKeyLines = @()
            $sortedKeys = $newKeys.Keys | Sort-Object
            foreach ($key in $sortedKeys) {
                $value = EscapeJsonValue($newKeys[$key])
                $newKeyLines += "  `"$key`": `"$value`","
            }

            # Remove trailing comma from last new key
            if ($newKeyLines.Count -gt 0) {
                $lastIdx = $newKeyLines.Count - 1
                $newKeyLines[$lastIdx] = $newKeyLines[$lastIdx].TrimEnd(',')
            }

            # Insert new keys before closing brace
            # Remove the closing brace line, append new keys, then closing brace
            $resultLines = @()
            for ($i = 0; $i -lt $closingBraceIdx; $i++) {
                $resultLines += $newLines[$i]
            }
            foreach ($nk in $newKeyLines) {
                $resultLines += $nk
            }
            $resultLines += "}"

            # Handle trailing comma on previous line if any (remove it since we're adding more)
            if ($resultLines.Count -gt $newKeyLines.Count + 1) {
                $prevLineIdx = $closingBraceIdx - 1
                if ($prevLineIdx -ge 0 -and $resultLines[$prevLineIdx] -match ',\s*$') {
                    $resultLines[$prevLineIdx] = $resultLines[$prevLineIdx] -replace ',\s*$', ''
                }
            }

            $output = $resultLines -join "`n"
            [System.IO.File]::WriteAllText($file.FullName, $output, [System.Text.Encoding]::UTF8)

            Write-Host "[$($file.Name)] SUCCESS (removed $removedCount, added $($newKeys.Count))" -ForegroundColor Green
            $successCount++
        } else {
            Write-Host "[$($file.Name)] FAILED: Could not find closing brace" -ForegroundColor Red
            $failCount++
        }
    } catch {
        Write-Host "[$($file.Name)] FAILED: $_" -ForegroundColor Red
        $failCount++
    }
}

Write-Host ""
Write-Host "Done. Success: $successCount, Failed: $failCount" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })