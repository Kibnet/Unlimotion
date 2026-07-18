# SPEC: Исполняемый BDD-мост remote-аутентификации Git (SC-0010-002)

## Контекст

`SC-0010-002` и правило `GR-029` требуют SSH и token/HTTP варианты аутентификации, включая выбор SSH key storage. Действующие `BackupViaGitServiceTests` изолированно проверяют преобразование remote, credentials и configured key storage, но не исполняются как единый Gherkin-сценарий.

## Объём

- Добавить тестовый контракт, step definitions и исполняемый сценарный тест для `SC-0010-002`.
- Связать мост с четырьмя passing `BackupViaGitServiceTests`: SSH remote, token/HTTP remote, SSH credentials и configured key storage.
- Дополнить `storm.json` и шесть текущих STORM-отчётов для `TS-0061` и `SD-0139`--`SD-0142`.

## Вне объёма

- Не менять production code, существующие tests, test annotations, `.feature`, acceptance criteria, реальные credentials, SSH keys или remote repositories.
- Не включать `GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows`: это отдельная ACL hardening проверка, которая зависит от текущего Windows sandbox.

## Критерии приёмки

1. Новый тест исполняет четыре шага `SC-0010-002` и проверяет `SD-0139`--`SD-0142`.
2. Контракт выполняет четыре связанных passing проверки; любой сбой делает BDD-сценарий failing.
3. После `/storm:bdd-sync` и `/storm:bdd-lint` сценарий имеет статус `passing` и связан с `TS-0061`, сохраняя `TS-0008/TS-0009`.
4. Проходят сборка тестового проекта, новый BDD-тест, четыре адресных метода, валидатор STORM и `git diff --check`.

## Риски и откат

Bridge создаёт только временные local Git repositories и key-файлы внутри test fixture. Откат ограничен новым тестовым кодом и artifacts; production и внешние remote не затрагиваются.
