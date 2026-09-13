# Исправления review к CLI и NuGet workflow

## Цель

Устранить два P1-замечания PR #293: относительный `TaskStorage.Path` должен разрешаться от каталога `Settings.json`, а release tag в PowerShell workflow должен передаваться через environment variable.

## Контракт

- `--tasks` сохраняет приоритет; абсолютный persisted path не изменяется.
- Относительный persisted path canonicalized относительно `DirectoryName(settingsPath)`.
- `RELEASE_TAG` передаётся через step `env`; `$env:RELEASE_TAG` является единственным источником tag в release-validation script.
- Package id/version, команда `unlimotion-cli`, schema settings и NuGet credential boundary не меняются.

## Проверка

- `TaskDirectoryResolverTests` покрывает relative path, absolute path и explicit override.
- `NuGetCliWorkflowContractTests` не допускает прямую GitHub-expression interpolation в `$tag` PowerShell source.
- Выполняются build, YAML parse/static inspection, pack и local tool smoke; CI PR — обязательный gate.

## Non-goals

Нет изменений UI, миграции task data, нового NuGet release или изменения секретов.

## Approval

Подтверждено пользователем: «Спеку подтверждаю» (2026-09-13).
