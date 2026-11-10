# Compact history panel without persistent commit selection

## Outcome and scope

Collapsing task history leaves its header without the extra bottom space. Clicking
or navigating commit rows does not leave a selected row background. SHA copy,
inline diff actions, focus, scrolling and virtualization remain available.

The approved implementation sets `MinHeight="28"` only on `TaskHistoryExpander`
and clears selection only for the history list's own nonempty selection events.
It does not change the shared expander template, operation-details panel, other
lists, history provider, storage or public APIs.

## Acceptance and local validation

Validated on base `01c4118f8022bfb4d247defc614641881e30834f` with .NET SDK
10.0.403, Avalonia 12.0.4, AppAutomation 1.9.0 and TUnit 1.44.0.

| Check | Result |
| --- | --- |
| Rendered Headless regression, light/dark at widths 1200/650 | 1/1 passed; collapsed expander 28 DIP, section 34 DIP, bottom gap 0 |
| Existing task-history UI tests | 8/8 passed |
| Git history provider/model tests | 27/27 passed |
| Windows native FlaUI history scenario, 216 DPI | 1/1 passed; no selected commit after pointer/keyboard, collapsed height 63 physical pixels = 28 DIP |

The rendered regression uses 61 real Git commits and exercises paging, repeated
pointer input, keyboard navigation, accessible names, nested SHA/diff actions,
large header text, both scroll regions, virtualization, status/loading/empty/error
states and the neighboring operation-details panel. Existing ListBox TabNavigation
behavior is preserved. Relevant Release builds passed.

Before/after videos and inspected PNG/TRX evidence are retained in the local-only
`20261110-exec` evidence bundle; they are not embedded in this repository:

- `before-native/history-flow.mp4`: failing reproduction on the original product.
- `delivery-native1/history-flow.mp4`: passing native scenario after the fix.
- Both videos: 2672x1722, 15 FPS, no audio; automated UI input.
- `after-headless4/`: rendered screenshots, observations and child test results.
- `delivery-native1/`: native screenshots, observations and test results.

## Review, risks and rollback

Scoped self-review: PASS. Selection is cleared synchronously with source/empty
guards, without disabling input or consuming routed events. The original shared
template and neighboring panel retain their behavior. Test-only DPI contexts are
restored after placement/capture, and native window size is bounded by the desktop work area, and rendered sessions use the existing isolated
child-process pattern. Fixture failures encountered during authoring were fixed
and the affected tests rerun successfully.

No storage migration or rollout is needed. Revert the change to restore the prior
layout and selection behavior. Local validation is distinct from GitHub CI and
does not imply installation or release.
