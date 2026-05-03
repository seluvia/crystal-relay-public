# Translating Crystal Relay

Thank you for helping make Crystal Relay easier to use in more languages.

Crystal Relay uses English as the source language and stores app translations as JSON files:

```text
VrcTwitchOscBridge/Resources/Localization/en-US.extra.json
VrcTwitchOscBridge/Resources/Localization/*.extra.json
```

Translation help is handled through GitHub issues and pull requests so contributors can help without needing a separate translation service account.

## Ways To Help

### Easiest: Open A Translation Issue

Use this when you do not want to edit files directly.

1. Open a new GitHub issue.
2. Choose the **Translation help** template.
3. Pick the language.
4. Paste the English text or screenshot text.
5. Paste your suggested translation.

This is the best path for beta testers and native speakers who want to help with wording.

### Direct: Submit A Pull Request

Use this when you are comfortable editing JSON files.

1. Fork the public repository.
2. Edit the matching file under:

```text
VrcTwitchOscBridge/Resources/Localization/
```

3. Translate values only.
4. Keep JSON keys unchanged.
5. Open a pull request.
6. Use a title like:

```text
Translation: improve Spanish wording
```

## What To Translate

- Translate the JSON values.
- Keep the JSON keys unchanged.
- Keep placeholders exactly as written, including order and spelling:
  - `{0}`
  - `{1}`
  - `{2}`
  - `{0:N0}`
- Keep product and platform names recognizable:
  - Crystal Relay
  - Twitch
  - VRChat
  - OSC
  - OSCQuery
  - Bits
  - Cheer
- Keep command and example syntax usable:
  - `!rewards`
  - `Cheer100 grow`
  - `Cheer100 shrink`
  - `VRC:`
  - `/avatar/parameters/...`

## Do Not Translate

Do not translate or rewrite:

- OAuth tokens, auth text, cookies, secrets, or private account details.
- OSC paths such as `/avatar/eyeheight`.
- JSON keys.
- Placeholder tokens like `{0}`.
- The `VRC:` reward prefix.
- Twitch command examples unless the surrounding sentence needs natural wording.

## File Guide

| File | Language |
| --- | --- |
| `en-US.extra.json` | English source |
| `es-ES.extra.json` | Spanish |
| `ja-JP.extra.json` | Japanese |
| `de-DE.extra.json` | German |
| `fr-FR.extra.json` | French |
| `pt-BR.extra.json` | Portuguese (Brazil) |
| `sv-SE.extra.json` | Swedish |
| `it-IT.extra.json` | Italian |
| `zh-CN.extra.json` | Simplified Chinese |
| `zh-TW.extra.json` | Traditional Chinese |
| `ko-KR.extra.json` | Korean |
| `ru-RU.extra.json` | Russian |
| `pl-PL.extra.json` | Polish |
| `th-TH.extra.json` | Thai |

## Quality Checks

Before a translation is released, Crystal Relay runs the localization audit.

Contributors can run it locally if they build from source:

```powershell
dotnet run --project .\LocalizationAudit\LocalizationAudit.csproj --no-restore
```

This catches missing strings, malformed JSON, untranslated fallback text, and placeholder mistakes.

If you cannot run this command, that is okay. The project maintainer can run it during review.

## Review Style

Good translations should be:

- natural for the language,
- clear to streamers who are not technical,
- short enough to fit inside buttons, dropdowns, and compact cards,
- consistent with nearby Crystal Relay wording,
- careful with safety warnings and Twitch/VRChat account language.

If a phrase is hard to translate because the English wording is unclear, open an issue or leave a note in the translation pull request.
