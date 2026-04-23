# AGENTS override

## UI testing

- MUST use UI tests when fixing bugs or developing new features that involve UI behavior, visual user flows, or UI-facing state changes.
- MUST add or update the relevant UI test coverage as part of the change. Prefer existing AppAutomation Headless/FlaUI tests or Avalonia.Headless tests, matching the affected flow and repository patterns.
- MUST run the relevant UI tests before finishing the task, or explicitly report why they could not be run.
