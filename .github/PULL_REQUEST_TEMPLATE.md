## Summary

Describe what this PR changes.

## Translation Checklist

If this PR changes translations:

- [ ] I edited only translation values, not JSON keys.
- [ ] I kept placeholders exactly the same, such as `{0}`, `{1}`, `{2}`, and `{0:N0}`.
- [ ] I kept command examples and technical tokens usable, such as `!rewards`, `Cheer100 grow`, `VRC:`, and `/avatar/parameters/...`.
- [ ] I did not add secrets, tokens, cookies, local app data, screenshots, or private account details.
- [ ] I checked the wording fits short UI areas such as buttons, dropdowns, and compact cards.

Optional local check:

```powershell
dotnet run --project .\LocalizationAudit\LocalizationAudit.csproj --no-restore
```
