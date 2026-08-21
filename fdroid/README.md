# F-Droid submission runbook

Этот каталог содержит upstream-черновик metadata для `com.Kibnet.Unlimotion`. Канонический файл F-Droid после внешнего review должен находиться в `fdroiddata/metadata/com.Kibnet.Unlimotion.yml`.

## Что уже отделено от обычной Android-сборки

- `FdroidBuild=true` исключает GitHub APK updater, `REQUEST_INSTALL_PACKAGES` и update `FileProvider`.
- F-Droid-вариант собирается только для `android-arm64`.
- Изменённый Nodify package собирается из публичного submodule commit `a8c9a96c80bc5e666aa34c9d3ce5947376e37722`.
- OpenSSL `3.0.21`, libssh2 `1.11.1` и libgit2 `1.6.5` собираются из исходников; F-Droid native package не использует готовый upstream `LibGit2Sharp.NativeBinaries.nupkg`.
- Recipe удаляет неиспользуемые `libgit2` test/fuzzer fixtures и его Node manifest до scanner-а; production sources остаются и CMake всё равно запускается с `BUILD_TESTS=OFF` и `BUILD_FUZZERS=OFF`.
- Avalonia build telemetry отключена через `AVALONIA_TELEMETRY_OPTOUT=1`.

## Локальная проверка

Из корня репозитория:

```powershell
pwsh -NoProfile -File scripts/test-fdroid-publication.ps1
pwsh -NoProfile -File scripts/test-android-build-scripts.ps1
```

F-Droid Android build с source-built заменами project-local Nodify/native packages выполняется в Bash с установленными Android SDK/NDK и .NET Android workload. Остальные managed зависимости по-прежнему восстанавливаются через NuGet; допустимость этой модели должен подтвердить F-Droid BuildServer/reviewer:

```bash
VERSION_NAME=1.28.0 VERSION_CODE=1028000 \
  bash ./scripts/build-fdroid-android.sh
```

Ожидаемый unsigned APK:

```text
artifacts/fdroid/Unlimotion-1.28.0-1028000-android-arm64.apk
```

Первый recipe закрепляется на отдельном source commit, содержащем build pipeline и Fastlane metadata. Будущий release tag `1.28.0` должен указывать именно на этот source commit; более поздний commit с самим upstream-черновиком recipe остаётся delivery-документацией и не входит в собираемый source snapshot.

## Проверка через fdroidserver

После того как source commit из metadata опубликован и доступен по `https://github.com/Kibnet/Unlimotion.git`:

```bash
fdroid lint com.Kibnet.Unlimotion
fdroid scanner com.Kibnet.Unlimotion
fdroid build --server com.Kibnet.Unlimotion:1028000
```

Обычный запуск Docker-контейнера или локальный `dotnet build` не считается эквивалентом успешного `fdroid build --server`.

## Выбор MR или RFP

- Если lint, scanner и `fdroid build --server` завершились успешно, recipe можно переносить в fork `fdroiddata` и готовить Merge Request.
- Если официальный BuildServer не поддерживает .NET 10/Android workload или NuGet restore, нужно создать Request For Packaging (RFP), приложив точную команду, полный лог и сведения ниже. Непроверенный recipe нельзя называть buildable.

Минимальный RFP payload:

```text
Application: Unlimotion
Package ID: com.Kibnet.Unlimotion
License: MIT
Source: https://github.com/Kibnet/Unlimotion
Candidate version: 1.28.0 (1028000)
Build variant: FdroidBuild=true, android-arm64
Known packaging issue: <exact fdroidserver/.NET/NuGet error and log link>
```

## Подпись и переход с GitHub APK

F-Droid подписывает APK своим ключом. Такая подпись отличается от подписи APK, опубликованного в GitHub Releases. Android может запретить установку одной сборки поверх другой с тем же package ID. Перед переходом пользователь должен сохранить резервную копию данных и при необходимости удалить GitHub-сборку перед установкой F-Droid-сборки.

Byte-for-byte reproducible build и использование upstream signing key не входят в первую итерацию.

## Внешний delivery gate

Push ветки, merge, tag `1.28.0`, GitHub Release, fork/MR в `fdroiddata` или отправка RFP выполняются только после отдельного подтверждения пользователя. Локальные проверки не дают разрешения на публикацию от его имени.

Даже после принятия metadata F-Droid указывает обычный срок появления приложения в каталоге около 24–48 часов после merge. Это ориентир внешнего сервиса, а не гарантия срока.
