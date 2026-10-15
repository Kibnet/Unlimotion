# Проверка native Git на Android Release

Этот тест проверяет настоящий LibGit2Sharp в отдельном пакете
`org.unlimotion.nativegitsmoke`. Обычный продукт не содержит Probe и negative hook.
Нужны Python3, Android SDK (adb/aapt), настроенный Android .NET SDK, API34 x64
эмулятор `codex_loading_profile_20261013`. Физическое ARM64 устройство и удалённый
Git/TLS этим тестом не проверяются.

Все output-каталоги должны быть новыми. `prepare` создаёт копию MainActivity с
двумя точными подстановками и targets во внешнем каталоге; исходник не меняется.
Targets запрещают сборку Probe с другим ApplicationId.

```powershell
python scripts/android-native-git-smoke/run.py prepare --source C:/path/to/build-source --output D:/private/probe-overlay
dotnet publish src/Unlimotion.Android/Unlimotion.Android.csproj -c Release -o D:/private/probe-apk -p:ApplicationId=org.unlimotion.nativegitsmoke -p:CustomAfterMicrosoftCommonTargets=D:/private/probe-overlay/probe.targets -m:1 --disable-build-servers -p:UseSharedCompilation=false
python scripts/android-native-git-smoke/run.py run --adb C:/path/to/adb.exe --aapt C:/path/to/aapt.exe --serial emulator-5580 --apk D:/private/probe-apk/org.unlimotion.nativegitsmoke-Signed.apk --output D:/private/probe-run
python -B -m unittest discover -s scripts/android-native-git-smoke -p test_protocol.py
```

Сборку выполнять из `build-source`; проверить фактические SDK/AOT/ABI, исходники
и managed payloads подписанного APK. Runner проверяет идентификатор/manifest до
установки. Он устанавливает только собственный тестовый пакет, выполняет два
положительных старта в разных процессах и один отрицательный. Каждый положительный
старт создаёт два отдельных private repo: Init → Stage → Commit → clean Status →
закрытие → открытие → точное чтение commit/blob. Повторная Configure между ними
не должна менять process handles. Negative hook действует до первой загрузки
через sentinel внутри тестового пакета; ожидаются явная bootstrap диагностика и
ошибка NativeMethods. JSON от другого PID, неполные операции и ложный success
отклоняются. Контроли `test_protocol.py` проверяют только admission, не работу Git.

`collectionComplete` означает завершение сбора. Обязателен просмотр ранних PNG,
видео и window dumps: реальное открытие списка, уведомления и отсутствие чёрного
экрана нельзя вывести из JSON. Следом отдельно проверить обычный APK без Probe
на рабочей копии fixture: loading → список → карточка, ownedPID logs, неизменность
task records. Не использовать длительность VFR видео как latency. До/после
артефакты с пользовательскими данными хранить приватно. Этой проверкой не закрывать
критерии производительности 5/1/5 или физическую приёмку Fold6.

Runner не удаляет репозитории или task records, не делает `pm clear`/uninstall.
Останавливает только собственное приложение. При незавершённом recorder/ADB
сбор признаётся неуспешным; перед новой попыткой проверить отсутствие собственного
screenrecord и завершение процесса приложения. Не перезаписывать failed evidence.
