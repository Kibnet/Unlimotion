# README Media Capture

This harness regenerates the desktop screenshots and the tab tour GIF used by `README.md` and `README.RU.md`.

## What it uses

- `Unlimotion.AppAutomation.TestHost` for a deterministic synthetic demo dataset
- `AppAutomation` page objects from `Unlimotion.UiTests.Authoring`
- `FlaUI` for the visible desktop run

## Default output

- Stable inspection output: `artifacts/readme-media/latest/en/` and `artifacts/readme-media/latest/ru/`
- Committed media targets: `media/readme/en/` and `media/readme/ru/`

## Typical command

```powershell
scripts/update-readme-media.ps1
```

The script:

- rebuilds the relevant UI test projects and the media generator
- runs the headless and FlaUI UI tests sequentially
- clears `artifacts/readme-media/latest/`
- regenerates English and Russian README screenshots plus `tab-tour.gif`
- copies successful assets into `media/readme/en/` and `media/readme/ru/`

## Direct command

```powershell
dotnet run --project tests/Unlimotion.ReadmeMedia/Unlimotion.ReadmeMedia.csproj -c Debug -- --copy-to-media --output-root artifacts/readme-media/latest
```

## Notes

- The harness always uses the `ReadmeDemo` synthetic scenario and generates both English and Russian variants by default. Use `--language en`, `--language ru`, or `--languages en,ru` to narrow the run.
- The generated `png` files and `tab-tour.gif` are copied into language-specific `media/readme/<language>/` directories only when `--copy-to-media` is passed.
- Root `report.json` records the multilingual run. Each language output directory also contains its own `report.json`.
- Roadmap capture uses the same visible desktop window as the other screenshots in the FlaUI path.
- Keep `README.md` and `README.RU.md` aligned with the generated file names when adding or removing capture steps.
