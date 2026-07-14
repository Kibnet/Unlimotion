# SPEC: Исполняемый BDD-мост подключения Git backup (SC-0010-001)

## Контекст

`SC-0010-001` и правило `GR-028` требуют, чтобы настройки позволяли предварительно проверить remote Git repository и безопасно подготовить подключение. Существующие `BackupViaGitServiceTests` покрывают preview, необходимость подтверждения и оба базовых connect flow, но не исполняются как единый BDD-сценарий.

## Объём

- Добавить тестовый контракт, step definitions и исполняемый сценарный тест для `SC-0010-001`.
- Связать мост с публичными тестами `BackupViaGitServiceTests`, гарантированно освобождая disposable fixture.
- Дополнить `storm.json` и шесть текущих STORM-отчётов для `TS-0060` и `SD-0135`--`SD-0138`.

## Вне объёма

- Не менять production code, существующие tests, test annotations, `.feature`, acceptance criteria, Git credentials или remote repositories.
- Не покрывать SSH/token, resolution conflicts или автоматические backup jobs: это отдельные сценарии `SC-0010-002..004`.

## Критерии приёмки

1. Новый тест исполняет четыре шага `SC-0010-001` и проверяет `SD-0135`--`SD-0138`.
2. Контракт выполняет preview/connect проверки для пустого и непустого remote; любой сбой делает BDD-сценарий failing.
3. После `/storm:bdd-sync` и `/storm:bdd-lint` сценарий имеет статус `passing` и связан с `TS-0060`, сохраняя `TS-0008/TS-0009`.
4. Проходят сборка тестового проекта, новый BDD-тест, четыре связанные методы `BackupViaGitServiceTests`, валидатор STORM и `git diff --check`.

## Риски и откат

Bridge работает только с временными локальными Git repositories, которые создаёт fixture. Полный класс содержит отдельную Windows ACL-проверку SSH-ключа, не относящуюся к `SC-0010-001`; её sandbox-сбой не является доказательством состояния Git-connect сценария. Откат ограничен новыми тестовыми и artifact-файлами; production и внешние remote не затрагиваются.
