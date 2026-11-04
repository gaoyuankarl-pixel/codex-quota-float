# Contributing

Build on Windows with `.\build.ps1`; package with `.\package.ps1`. Use C# 5 syntax and .NET Framework 4.8. No SDK or NuGet dependencies are required.

## Validation

`--self-test` covers quota classification, missing/zero values, language migration, ring drawing/direction, host lifecycle, foreground rules, startup quoting, and persistent reset idempotency. **Never redeem a real reset credit in automated tests.**

For UI checks, run `app\CodexQuotaFloat.exe --demo --qa --lang=en`, then repeat with `--lang=zh-CN`. Demo bypasses the foreground gate but does not query the account. Check labels, countdowns, hover opacity, menus, and language switching. The language is remembered.

`tests\render-preview.ps1` renders the actual WPF card with synthetic data to documentation PNGs without showing a window or querying an account.

The following optional **interactive integration checks** register/restart this tool's watcher and require Explorer; they do not close Codex desktop or redeem credits:

- `tests\verify-launch-isolation.ps1`: temporary Windows Job verifies the Explorer-launched watcher survives launcher cleanup.
- `tests\verify-startup-readiness.ps1`: exercises the installed shortcut with a delayed synthetic executable.

## Changes

- Match windows by duration, not primary/secondary order. Missing values must not look unlimited or zero.
- Preserve reset persistence before submission, account/context verification, same-key retries, and server-authoritative outcomes.
- Do not commit credential files, raw protocol logs, runtime data, account IDs, local diagnostics, or personal screenshots.
- Add translations in `src/L10n.cs`. Existing snapshots must update labels when the language changes.
- Explain visible behavior and relevant checks in pull requests; include only synthetic screenshots.

For a release, update `Program.Version`, `app.manifest`, download examples, and `CHANGELOG.md` together. Push a matching `vX.Y.Z` tag after the Windows build succeeds. The workflow verifies the source version and publishes the ZIP and checksum.

## 中文

在 Windows 运行 `build.ps1` 与 `package.ps1`，保持 C# 5 兼容。自动测试只用模拟数据，绝不消耗真实重置。界面可用 `--demo --qa --lang=en` / `--lang=zh-CN` 检查。提交前检查语言切换、未知值、前台限制及幂等重试，不要提交运行数据、账户信息或个人截图。欢迎 Issue 和 Pull Request。
