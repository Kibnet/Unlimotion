# Исправление исчезновения эмодзи после изменения заголовка карточки

## 0. Метаданные

- Профили: `dotnet-desktop-client`, `ui-automation-testing`; context — `testing-dotnet`.
- Владелец: агент; исходный запрос пользователя от 02.10.2026.
- Масштаб: medium. Expanded SPEC по центральному `templates/specs/_template.md`: общий распознаватель используется фильтрами, наследованием и рендерингом; расширяется набор публичного `EmojiPattern`, нужны межкомпонентные проверки совместимости, поэтому short не выбран.
- Behavior baseline каталога: GPT-6 Astra; поверхность — Codex desktop. Точный model ID/effort текущей сессии не установлен и не влияет на контракт продукта. Model eval — не применимо, задача о .NET/Avalonia.
- Проверенная база: `main`, HEAD `46711e60d6ef453e794104ee7d9f81ad6f37c68c`; перед SPEC рабочее дерево чистое. Remote не обновлялся.
- Фаза: EXEC PASS, 04.10.2026. Точная пара `🧙‍♂️ → 🪼` подтверждена; отдельная regression coverage, последняя remediation и оба полных проекта завершены. CI source660ee145 также GREEN. PR #315 обновляется по поручению «Оформи PR»; окончательный delivery read-back фиксируется ниже. Причина совпадает с утверждённым parser defect; product scope не меняется. Установка не поручена.
- Central stack: `routing-matrix`, `creator-vibe-lens`, `model-behavior-baseline`, `tool-execution-baseline`, `collaboration-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, выбранные профили, `spec-linter`, `spec-rubric`, `review-loops`. Локальный `AGENTS.override.md` требует добавить/обновить и выполнить UI-тесты. Скилл — `appautomation`.
- Источники: `src/Unlimotion.ViewModel/EmojiTextHelper.cs`, `TaskItemViewModel.cs`, `MainWindowViewModel.cs`; `src/Unlimotion/EmojiTextBlock.cs`, `Views/MainControl.axaml`; текущие тесты карточки и фильтров.
- Unicode: [emoji-test.txt 17.0](https://www.unicode.org/Public/17.0.0/emoji/emoji-test.txt), [UTS #51](https://www.unicode.org/reports/tr51/). Версия зафиксирована; утверждения о поддержке будущих версий нет.

## 1. Overview / Цель

Исходный симптом: «При изменении эмодзи у карточки, она пропадает из фильтров и из карточек детей».

Success means: после замены эмодзи в заголовке распознаваемый символ целиком доступен в include/exclude-фильтрах и отражается в цепочке родителей дочерних карточек без перезапуска и ожидания автосохранения. Сохранение и повторная загрузка сохраняют этот результат.

Output: исправление общего распознавателя, regression/UI-тесты и просмотренные визуальные доказательства на синтетических задачах. На SPEC output — этот документ.

Stop rules: до exact approval изменять только эту SPEC. На EXEC сначала подтвердить RED исходного сценария, затем исправить и получить обязательные проверки. Если окажется, что конкретный случай пользователя имеет другую причину, уточнить диагноз и применить gate для существенного изменения scope; не объявлять исходный случай исправленным только по тесту нового символа. Commit/push/PR, установка, публикация и редактирование личных задач отдельным поручением не разрешены.

## 2. Текущее состояние (AS-IS)

1. `TaskItemViewModel.Emoji` вычисляется через `EmojiTextHelper.ExtractEmoji(Title)`. `TitleWithoutEmoji`, `OnlyTextTitle` и `EmojiTextBlock` используют тот же helper.
2. `MainWindowViewModel`, регион Emoji, группирует задачи по строке `Emoji` и создаёт отдельные include/exclude-коллекции. Пустой ключ соответствует «Все», а не отдельному emoji-пункту.
3. Подписка на `Title` уже вызывает `RecalculateEmoji()` и обновление вычисляемых полей всех потомков. `ParentEmojiTrail` строится из эмодзи всех предков; `GetAllEmoji` участвует в фильтрации. Обход потомков защищён от циклов visited-набором ID.
4. Поэтому пустой результат распознавания одновременно убирает эмодзи из самостоятельного пункта фильтра и из цепочки родителей у детей. Наличие подписок не доказывает корректность всех переходов UI; это нужно проверить на EXEC.
5. Read-only выполнение действующего регулярного выражения подтвердило: 🧭 распознаётся; 🫶, 🪿, 🛝, 🫩 дают пустую строку. Для `🐦‍🔥`, `🙂‍↔️`, `🙂‍↕️` извлекаются отдельные части, теряется U+200D. Сравнение выполнялось через `StringComparison.Ordinal`.
6. Сверка с официальным корпусом 17.0: из 3944 `fully-qualified` строк полностью совпали 3527, 417 отсутствуют либо распознаны частично. Из 5216 форм `fully-qualified`, `minimally-qualified`, `unqualified` полностью совпали 4596, 620 имеют дефект; у 72 результат совсем пустой. SHA-256 UTF-8 текста, полученного read-only: `1d8a944f88d7952f7ef7c5167fef3c67995bcae24543949710231b03a201acda`.
7. Существующий UI-тест `CurrentTaskCard_ParentEmojiTrail_RefreshesWhenAncestorTitleChangesBeforeSave` проверяет 🧭, но не новую эмодзи и не появление соответствующего пункта фильтра.
8. На этапе SPEC конкретная пара пользователя была неизвестна. 04.10.2026 получена `🧙‍♂️ → 🪼`; read-only проверка указанного пользователем коммита в локальном пространстве задач подтвердила замену при сохранении остального текста Title. Старое выражение извлекает `🧙‍♂️` до изменения и пустую строку после; исправленное выражение извлекает `🪼`. Личные заголовки, ID и Git history пространства задач не публикуются; regression использует синтетический текст «Проект».

## 3. Проблема

Общий список распознаваемых эмодзи неполон и разрывает часть составных последовательностей. Все зависящие от него представления получают пустое или неполное значение при корректном заголовке.

## 4. Цели дизайна

Один согласованный источник распознавания для извлечения, удаления и сегментации; сохранение точных UTF-16 последовательностей; совместимость старых форм и порядка эмодзи; отсутствие сетевых запросов в приложении и блокирующей работы в UI-потоке.

## 5. Non-Goals

Новое дерево фильтров, новая семантика выбора категорий, перенос выбранного старого ключа на новый, нормализация сохранённых заголовков, изменение связей/статусов/хранилища, миграция данных, замена emoji-шрифта, новая палитра, синхронизация или CLI. Отображение шрифтом всех будущих Unicode-символов не обещается.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `EmojiTextHelper.cs`: сохранить сигнатуры `ExtractEmoji`, `RemoveEmoji`, `Split` и тип публичной константы `EmojiPattern`; расширить её набор новым корпусом и сохранить прежний шаблон как приватный legacy fallback.
- Новый `EmojiUnicodeData.g.cs` рядом с helper: статический компактный шаблон последовательностей из зафиксированного Unicode 17.0; комментарий с источником/версией. Генерация производится при разработке, без runtime-зависимости и без отдельной универсальной инфраструктуры.
- Тестовый корпус `src/Unlimotion.Test/TestData/Unicode/emoji-test-17.0.txt`: исходные данные с лицензией и checksum, доступные тестам без сети.
- Новые helper-тесты и UI-регрессии в существующих классах карточки/фильтров. Production ViewModel-подписки не менять без failing evidence.

### 6.2 Детальный дизайн и visual planning artifact

Генерируемый внутренний шаблон объединяет формы `fully-qualified`, `minimally-qualified`, `unqualified` из корпуса. Внутри него общие префиксы компактно группируются, продолжения стоят перед терминальной короткой ветвью.

Полный публичный `EmojiPattern` — корпусный шаблон первым, прежний шаблон вторым как legacy fallback; compiled regex остаётся один. Прежний текст выражения сохраняется в приватной константе. Все три метода используют один согласованный regex, и прямой `new Regex(EmojiPattern)` получает тот же полный набор. Новый корпусный длинный `🐦‍🔥` должен совпадать раньше старого префикса `🐦`. ASCII-цифры, `#` и `*` распознаются только внутри keycap-последовательности. Не использовать широкие диапазоны всех символов/суррогатных пар как определение эмодзи.

Два regex с отдельным matcher не вводятся без подтверждённой необходимости. Review-предположение о legacy-форме `😀` + U+FE0F было опровергнуто контрольным запуском: и старый, и corpus-first regex захватывают 2 из 3 UTF-16 code units; лишний selector раньше тоже оставался текстом. Не называть этот случай регрессией нового решения. Старые поддерживаемые формы/компоненты проверяются отдельно; если реальный counterexample обнаружится на EXEC, скорректировать matcher в границах того же output contract.

`ExtractEmoji` возвращает последовательности в исходном порядке, со всеми модификаторами/VS/ZWJ/tag-кодами. `RemoveEmoji` удаляет ровно распознанные участки, не оставляет части корректной последовательности. Конкатенация `Split` должна восстанавливать исходный текст, а emoji-сегмент — содержать весь составной символ. Строку из нескольких самостоятельных эмодзи сохраняем единым ключом фильтра, как сейчас.

При смене заголовка используются имеющиеся подписки. Старый ключ исчезает только при отсутствии других его носителей, новый становится доступен. Выбор других ключей сохраняется. Выбранный старый ключ не переносится автоматически: выбор относится к эмодзи, а не к ID карточки. Поэтому список задач с активным старым фильтром может измениться законно; исправляем отсутствие нового пункта и наследования.

Storyboard ожидаемого результата на синтетических данных:

```text
До редактирования:     родитель «🧭 Проект»
Фильтры:              [ ] 🧭 Проект
Карточка ребёнка:     родительские эмодзи [🧭]   ID ребёнка

После замены в поле заголовка на «🫶 Проект»:
Фильтры:              [ ] 🫶 Проект
Карточка ребёнка:     родительские эмодзи [🫶]   тот же ID ребёнка
Выбор 🫶 в include:   родитель и наследующие эту эмодзи дети видны
Выбор 🫶 в exclude:  соответствующие задачи исключены
```

Ошибка и recovery: неизвестные будущие символы остаются в исходном тексте; приложение не должно падать. Пустой заголовок/удаление эмодзи даёт нормальное пустое значение. Не исправлять неподтверждённую подписочную проблему вместо подтверждённой неполноты распознавателя.

Производительность: статический compiled regex создаётся один раз; новый набор не загружается для каждой карточки. Проверить ограниченный длинный заголовок, повторённые legacy-only варианты и отсутствие патологического backtracking на префиксах составных эмодзи. Числовой выигрыш производительности не заявляется.

Видео: репозиторий умеет записывать FlaUI-сценарии (`record-status-contract-evidence.ps1`), но готового сценария смены эмодзи нет. Preflight подтвердил доступность `ffmpeg`/`ffprobe`, interactive session 1; техническая невозможность записи пока не установлена. На EXEC определить возможность записи текущего regression run. Если используемый Avalonia.Headless runner не поддерживает видео и доступный recorder не позволяет безопасно записать этот regression run, зафиксировать конкретную техническую причину и предоставить до/после PNG из `CaptureRenderedFrame()` плюс RED/GREEN-логи. Отсутствие готового сценария само по себе не является оправданием fallback; доступный безопасный путь записи использовать.

### 6.3 User-Observable Scenarios

| Сценарий | Действие | Видимый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| Исходный дефект | В реальном поле заголовка заменить 🧭 на 🫶 или полученную от пользователя эмодзи | Новый пункт в обоих popup; новая цепочка родителей у ребёнка/внука | UI RED/GREEN + просмотренные кадры | 1, 3 |
| Фильтрация | Выбрать новый ключ include, затем отдельно exclude | Состав списков соответствует текущим OR/include и exclude-правилам | UI/integration assertions | 4 |
| Повторное изменение | Новый символ → составной символ → без эмодзи → старый символ | Согласованное обновление обеих поверхностей без перезапуска | UI regression | 2, 3, 5 |
| Повторная загрузка | Сохранить синтетический родитель и заново загрузить fixture | Корректный новый ключ и наследование | FileStorage integration | 6 |

### 6.4 State / Interaction Matrix

| Состояние | Триггер | Результат / edge case |
| --- | --- | --- |
| Старый emoji-ключ | Замена на новый | Появляется новый; старый сохраняется, если есть другой носитель |
| Открытый popup | Изменение Title | Обновляется список и подпись; другие выбранные ключи сохраняются |
| Несколько родителей | Меняется один предок | Сохраняются эмодзи остальных предков, нет пропавших связей |
| Составная эмодзи | ZWJ/VS/skin-tone/tag | Полная исходная последовательность, без невидимых остатков в текстовой части |
| Нет эмодзи | Удаление / повторное добавление | Пункт исчезает / появляется; цепочка оставшихся предков сохраняется |
| Повторяющийся ключ | Переназначен один носитель | Уцелевшая категория продолжает работать |

### 6.5 Decision Ledger

| Решение | Owner | Выбор / основание | Confidence | Риск | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Обновлять общий распознаватель | agent | Общий источник двух симптомов, подтверждён read-only | 0.95 | Конкретный случай может быть иной | Нет |
| Зафиксировать Unicode 17.0 | agent | Проверенный официальный корпус, без сети в приложении | 0.95 | Более новые версии вне гарантии | Нет |
| Совместимость | agent | Новый longest-first корпус перед неизменным legacy expression, один regex | 0.90 | Нужны точные границы и проверки старых форм; усложнение без counterexample не требуется | Нет |
| Смысл выбора фильтра | agent | Сохранить выбор по ключу, не переносить на новую эмодзи | 0.95 | При активном старом ключе меняется список задач | Нет |
| Пользовательская пара эмодзи | user | Дополнительный пример; если поступит, включить в regression | — | Без ответа диагноз индивидуального случая не подтверждён | Нет, для подтверждённого дефекта |

### 6.6 Runtime / Config / Data Contract Matrix

JSON задач и настроек не меняется. Источник эмодзи — сохранённый `Title`; корпус используется только для генерируемого распознавания и тестов. Существующие ключи сохраняют исходные последовательности. Новая сеть, packages и storage API не требуются.

## 7. Данные и совместимость

Исходные заголовки не переписываются. Существующая concatenation-семантика нескольких эмодзи и порядок предков остаются. Публичная `EmojiPattern` остаётся const string и расширяет распознаваемый набор; прежнее выражение сохраняется как приватный fallback. Прямой regex и методы helper используют один контракт. Unicode-корпус и сгенерированные данные должны иметь проверяемое происхождение; тесты не зависят от доступности unicode.org.

## 8. Интеграции

Фильтры include/exclude и Graph получают новый ключ через текущие observable collections. Карточки детей используют текущий `ParentEmojiTrail`, списки — `GetAllEmoji`; рендерер — `Split`. Никакой параллельной системы эмодзи не добавляется.

## 9. Обработка ошибок

Пустой/null input, обычный русский/латинский текст, числа, пунктуация, неизвестные и некорректные последовательности не вызывают исключения. Тесты должны различать валидную эмодзи и случайный символ, а не только проверять непустой результат.

## 10. Миграция / Rollout / Rollback

Миграция не нужна: после запуска исправленной версии данные вычисляются из прежнего Title. Rollback — откат файлов распознавателя/тестов текущего изменения; задачи и связи не затрагиваются. Установка на компьютер и проверка пользовательского приложения не входят в эту SPEC.

## 11. Тестирование и критерии приёмки

1. **AC1:** все 5216 указанных форм из корпуса 17.0 извлекаются целиком при ordinal-сравнении; 🫶/🪿/🛝/🫩 не дают пустого результата.
2. **AC2:** корректные ZWJ/skin-tone/flags/keycaps/tag-последовательности не разрываются; `Split` восстанавливает точный input, `RemoveEmoji` оставляет только исходную текстовую часть. Новый полный `🐦‍🔥`, skin-tone формы и legacy-компоненты проверены точными Extract/Remove/Split assertions с соседним обычным текстом. Цифры, русский текст, обычная пунктуация не принимаются за эмодзи. Прямой regex из `EmojiPattern` соответствует helper на корпусе.
3. **AC3:** ввод нового заголовка через `CurrentTaskTitleTextBox` обновляет include/exclude-пункты и видимый `CurrentTaskParentEmojiTrail` ребёнка/внука до автосохранения. Проверяются несколько родителей и повторные смены.
4. **AC4:** выбор нового include/exclude-ключа корректно меняет видимые списки; посторонние выбранные ключи не сбрасываются. Совпадающий старый ключ остаётся доступным, если его использует другая задача.
5. **AC5:** старые формы, порядок нескольких эмодзи, переходы «эмодзи → пусто → эмодзи» и обновление распознаваемая → распознаваемая сохраняют ожидаемое поведение.
6. **AC6:** сохранение и повторная загрузка синтетического графа подтверждают новый ключ и цепочку родителей без изменения ID, связей и текста Title.
7. **AC7:** стандартная сборка и оба обязательных test projects проходят; визуальные состояния до/после просмотрены и соответствуют storyboard. Если обязательная проверка недоступна или падает, завершение EXEC не заявляется.

Обязательный набор: helper/corpus regression, затронутые UI-классы, обычная сборка desktop, весь `Unlimotion.Test` и `Unlimotion.UiTests.Headless`. Полный набор выбран из-за общего распознавателя: он влияет также на подписи, поиск, сортировку и рендеринг. Существующие проверки активного UI-фильтра не заменяются тестом свойства VM.

### Acceptance-to-Test Matrix

| AC | Автоматическая проверка | Визуальное / иное evidence | Результат EXEC |
| --- | --- | --- | --- |
| 1 | Новый `EmojiTextHelperTests`: офлайн-корпус и точные примеры | RED/GREEN и версия/checksum данных | PASS: `green/helper.log`, 23/23; 5216/5216 форм |
| 2 | Helper-тесты: Extract/Remove/Split и прямой regex, ordinal, longest-first/legacy boundaries, негативные случаи | Показательный составной символ в кадре | PASS: corpus, ZWJ/modifiers/flags/keycaps/tag и длинный prefix input |
| 3 | Regression в `MainControlTaskCardLayoutUiTests` и `MainControlFilterToolbarResponsiveUiTests`: ввод в TextBox, оба popup, ребёнок/внук | До/после кадров с новым пунктом и цепочкой | PASS: `remediation/ui-popup-no-capture-final.log` и `ui-popup-capture-final.log`, по 3/3; просмотрены все 27 `visual-popup-final` PNG; те же cases прошли в финальном полном main |
| 4 | UI/integration: выбор фильтров, независимый ключ, второй носитель старой эмодзи | Видимые списки и checkbox-состояния | PASS: recursive projected IDs + реальные TreeViewItem, selected PNG показывают ребёнка при include и его отсутствие при exclude |
| 5 | Helper и UI-последовательность повторных изменений | Удаление и повторное появление | PASS: четыре перехода на обоих popup и card/descendant tests |
| 6 | Новый `EmojiTitlePersistenceTests`: FileStorage fixture save/reload | Read-back Title/ID/relations и вычисляемых значений | PASS: `green/persistence.log` 1/1 |
| 7 | Desktop build, full main + full Headless, diff check | Просмотр PNG/video; отдельно результаты каждого suite | PASS на source dc160881/660ee145: desktop0 errors/2 warnings; main1181/1181, 19m31.277s; Headless51/51, 2m26.809s; оба exit0, 0 failed/skipped. Exact4/4, original card1/1, helper25/25; все27 visual-card-setup-final PNG просмотрены. CI660ee145: main1181/1181, Headless51/51, CodeQL и Android SUCCESS |

Плановые команды, выполняются последовательно после restore/toolchain preflight:

```powershell
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -p:UseSharedCompilation=false -- --treenode-filter "/*/*/EmojiTextHelperTests/*" --maximum-parallel-tests 1
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --maximum-parallel-tests 1
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --maximum-parallel-tests 1
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -p:UseSharedCompilation=false -- --treenode-filter "/*/*/EmojiTitlePersistenceTests/*" --maximum-parallel-tests 1
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -p:UseSharedCompilation=false -- --maximum-parallel-tests 1
dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -p:UseSharedCompilation=false -- --maximum-parallel-tests 1
git diff --check
```

Evidence сохранять под `artifacts/emoji-title-refresh/` на EXEC, с разделением RED/GREEN. Для текущего screenshot harness использовать `UNLIMOTION_TEST_CAPTURE_FRAMES=1` и `UNLIMOTION_TEST_TRACE_DIRECTORY` в соответствующем запуске. В UI-fixture управлять автосохранением детерминированно и подтвердить, что assertions выполнены до записи нового Title; отдельная persistence-fixture проверяет явное сохранение и загрузку. Перед длинным suite сообщить команду и лог/progress; после timeout исследовать причину, не повторять тот же запуск вслепую. После зелёного обязательного набора не расширять проверки без новой причины.

## 12. Риски и edge cases

Устаревший шрифт может не содержать glyph нового символа; это отдельный вопрос рендеринга, который нельзя скрывать непустым VM assertion. UI-сценарий выбрать на поддерживаемом bundled/system font символе и реально посмотреть. Изменение regex может задеть обычный текст или скорость; negative/corpus tests и ограниченная проверка длинных префиксов обязательны. Точный пользовательский пример может потребовать дополнительного диагноза.

### Expected User Review Objections

| Замечание | Почему вероятно | Как учтено | Статус |
| --- | --- | --- | --- |
| «У меня исчезает при другой эмодзи» | На SPEC конкретная пара была неизвестна | Полученная `🧙‍♂️ → 🪼` сверена с реальным commit и старым matcher; отдельные exact UI/persistence cases 4/4, все 27 rendered PNG просмотрены | resolved для сообщённой пары; installed-app repro отдельно не заявляется |
| «Тест значения прошёл, а на карточке всё равно пусто» | Общая VM и шрифт/controls имеют разные границы | Ввод через реальное поле, оба popup, видимая цепочка детей, просмотр rendered frames | mitigated |
| «Почему выбранный старый фильтр больше не показывает карточку?» | Выбор соответствует ключу эмодзи | Явно сохранить прежнюю семантику, предложить новый ключ; перенос выбора вне scope | mitigated |

## 13. План выполнения

После approval: повторить preflight; добавить офлайн-корпус и failing regression; подтвердить причину RED; обновить распознавание; получить targeted GREEN; проверить save/reload; выполнить обычную сборку и полный обязательный набор; посмотреть кадры/видео; выполнить post-EXEC review и обновить журнал.

## 14. Открытые вопросы

Блокирующих продуктовых решений для подтверждённого дефекта нет. Дополнительный диагностический вопрос закрыт 04.10.2026: получена `🧙‍♂️ → 🪼`, пример включён в отдельные regression cases. Причина совпала с утверждённой; новые продуктовые решения не требуются.

## 15. Соответствие профилю

Запланированы UI-тесты реального пользовательского пути, стабильные существующие AutomationId, стандартная сборка и TUnit-прогоны. На SPEC они не запускались и не считаются evidence исправления. Visual planning приведён в §6.2; видео/fallback требует фактического решения на EXEC.

## 16. Таблица изменений файлов

| Файл | Плановое изменение | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/EmojiTextHelper.cs` | Новый корпусный шаблон перед прежним приватным fallback, один публичный pattern/compiled regex | Общий дефект и совместимость старых форм |
| `src/Unlimotion.ViewModel/EmojiUnicodeData.g.cs` | Зафиксированные generated-последовательности | Офлайн-поддержка проверенного набора |
| `src/Unlimotion.ViewModel/Unicode-Emoji.LICENSE.txt`, `Unlimotion.ViewModel.csproj` | Полный Unicode notice в output/publish | Атрибуция generated data сохраняется при стандартной сборке/доставке |
| `src/Unlimotion.Test/TestData/Unicode/generate-emoji-pattern.py` | Детерминированный UTF-16 trie generator с checksum и longest-prefix self-check | Воспроизводимость generated data |
| `src/Unlimotion.Test/TestData/Unicode/emoji-test-17.0.txt` | Официальный тестовый корпус и license | Проверка всех форм без сети |
| `src/Unlimotion.Test/TestData/Unicode/LICENSE.txt`, `.gitattributes` | Лицензия Unicode и запрет преобразования строк raw-корпуса | Сохранение атрибуции и SHA-256 при checkout на Windows |
| `src/Unlimotion.Test/EmojiTitleTestAppBuilder.cs` | Real App/fonts + Skia + HeadlessDrawing=false для новых regressions | Просматриваемые кадры из UI-тестов |
| `src/Unlimotion.Test/EmojiTextHelperTests.cs` | Corpus, boundaries, legacy regression | Отличить исправление от неполной заплатки |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | Видимое обновление наследуемых эмодзи | Исходный симптом детей |
| `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` | Новый пункт и фильтрация после редактирования | Исходный симптом фильтров |
| `src/Unlimotion.Test/Unlimotion.Test.csproj` | Копирование test data при необходимости | Доступность корпуса из output |
| `src/Unlimotion.Test/EmojiTitlePersistenceTests.cs` | Сохранение и повторная загрузка синтетического графа | AC6 |
| `src/Unlimotion.Test/FileStorageTaskStatusTests.cs` | Nonblocking hydration pump и fault diagnostics; актуальная metadata65 seed tasks и последующих edits, прежние exact64 cutoff/10s limits; assertions отсутствия status/availability миграционных изменений и полного65 cache | Validation remediation AC7 по поручению03.10 |
| `src/Unlimotion.Test/ServerStorageCrudRealtimeContract.cs` | Readiness через authenticated GetAllTasks: первый ответ с исходным SDK transport timeout, затем10s visibility poll только успешных страниц без ID; SDK remaining timeout/token cleanup и failure-only5s index/state snapshot; все5 content/auth assertions прежние | Validation remediation AC7: исходный cold HTTP budget восстановлен; internal non-stale barrier и prewarm отклонены |
| `src/Unlimotion.Test/TestHelpers.cs` | Ожидание ожидаемого изменения количества для команд с nonzero delta | Validation remediation AC7: async confirmation deletion |
| `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` | Cache+disk deletion completion; async search waits с прежними predicates/15s | Validation remediation AC7: актуальные UI effects |

Изменение production-подписок, font binary или других модулей не запланировано; при новом evidence сначала определить, является ли это редакционной конкретизацией или существенным расширением scope.

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Новая эмодзи | Пустой/частичный ключ | Целый исходный ключ |
| Filter popup | Отсутствует новый пункт | Новый пункт доступен include/exclude |
| Дочерние карточки | Новый символ пропадает из цепочки | Новый символ отражается без перезапуска |
| Обычный текст и старые формы | Текущие правила | Совместимость защищена тестами |
| Публичный raw `EmojiPattern` | Историческое выражение | Полный набор, согласованный с методами helper |

## 18. Альтернативы и компромиссы

Добавить только несколько отсутствующих символов — маленький diff, но следующий символ воспроизводит тот же баг; не выбрано. Считать emoji любой pictographic/суррогатный диапазон — компактно, но ошибочно поглощает обычный текст и не задаёт составные последовательности; не выбрано. Зафиксированный официальный набор перед legacy fallback выбран ради полноты и совместимости; цена — generated data и тестовый корпус. Два regex/отдельный matcher отвергнуты как неподтверждённое усложнение; первоначальный counterexample reviewer отозван после контрольной проверки.

## 19. Результат quality gate и review

Ниже сохранена хронология SPEC/EXEC reviews. Актуальный итог exact-pair coverage и обязательных проверок находится в «Финальный Post-EXEC Review точной пары, 04.10.2026»; прежние pending/NEEDS-FIX и PASS относятся указанным в них редакциям.

### SPEC Linter Result

| № | Критерий | Статус | Evidence / обоснование |
| --- | --- | --- | --- |
| 1 | Наблюдаемый результат | PASS | §1 и §6.3: пункты фильтров и цепочка ребёнка после редактирования |
| 2 | AS-IS | PASS | §2: текущие production-файлы, точные regex examples и corpus baseline |
| 3 | Корневая проблема | PASS | §3: подтверждённая неполнота общего helper; индивидуальный случай обозначен гипотезой |
| 4 | Цели дизайна | PASS | §4: единое извлечение/удаление/сегментация и точные последовательности |
| 5 | Границы | PASS | §5: сохранена семантика фильтров, нет данных/релиза/установки |
| 6 | Ответственности | PASS | §6.1 и §16: конкретные production/data/test-файлы |
| 7 | Интеграция | PASS | §8: текущие observable pipelines и bindings |
| 8 | Алгоритм и инварианты | PASS | §6.2: corpus longest-first, продолжения до терминальной ветви, corpus-before-legacy, ordinal |
| 9 | Ошибки | PASS | §9: null, plain text, invalid/future input |
| 10 | Производительность | PASS | §6.2/§12: static compiled pattern, ограниченный длинный prefix test; ускорение не заявлено |
| 11 | Данные/состояние | PASS | §6.4/§6.6: переходы и Title source of truth |
| 12 | Совместимость | PASS | §7: JSON, const string API, legacy expression, прямой regex/helper и concatenation |
| 13 | Rollback | PASS | §10: откат change set без мутации задач |
| 14 | Измеримые AC | PASS | §11: семь критериев и точные числа корпуса |
| 15 | AC→evidence | PASS | Matrix: helper/UI/persistence/visual и негативные случаи |
| 16 | Команды/stop | PASS | TUnit filters/serial, обычный desktop build, полные suites; gates и timeout-порядок |
| 17 | План | PASS | §13: RED зависит от тестов, GREEN от fix, completion от обязательных проверок |
| 18 | Решения/вопросы | PASS | Ledger: внутренние defaults; конкретная пара — дополнительный пример |
| 19 | Масштаб/форма | PASS | §0: expanded из-за общего helper/межкомпонентной совместимости |
| 20 | Профиль | PASS | §15: UI-тесты, визуальный planning, обязательные build/test и честная фаза evidence |

Linter: ГОТОВО по содержанию. Итоговый post-SPEC stop decision остаётся ниже и учитывает отдельный review.

### SPEC Rubric Result

| Критерий | Балл | Основание |
| --- | ---: | --- |
| Цель и границы | 5 | Исходный симптом и обе UI-поверхности, отдельные Non-Goals |
| AS-IS | 5 | Проверены общий helper, downstream и официальный корпус |
| Дизайн | 5 | Один офлайн static pattern с корпусом и fallback, точные границы/совместимость |
| Безопасность/rollback | 5 | Нет миграции/записи личных задач, локальный откат |
| Проверяемость | 5 | RED/GREEN, семь AC, корпус, реальные controls, persistence |
| Автономность | 5 | Существенных user-owned решений для подтверждённого дефекта нет |

30/30 для описанного scope; баллы не подтверждают индивидуальную причину, техническую доступность видео или выполненную реализацию.

### Role-Based Review Result

| Роль | Применимость | Проверка | Вердикт / действие |
| --- | --- | --- | --- |
| Business analyst / domain | Не применимо | Нет новой бизнес-логики/процесса | — |
| UX / designer | Применимо | Новый пункт, новый parent trail, прежние checkbox-правила и макет | PASS; реально просмотреть кадры на EXEC |
| Tester / validation | Применимо | Корпус, negatives, ввод TextBox, повторения, второй родитель, save/reload | PASS; определения тестов/артефактов перечислены |
| Developer / architect | Применимо | Общий helper/const API, longest-first corpus и compatibility fallback | PASS после удаления неподтверждённого усложнения; shared subscriptions не менять без evidence |
| Delivery / operations / security | Применимо к границам | Только локальный SPEC/EXEC, синтетический dataset, video preflight | PASS; выпуска/установки нет, доступность записи не выдумана |

### Post-SPEC Review

- Статус: **PASS для фазы SPEC**. Можно запрашивать exact approval; реализация и её validation не выполнены.
- Scope reviewed: эта SPEC, перечисленный в §0 instruction stack и локальный UI override, helper/Title/parent-trail/filter pipelines, текущие UI-тесты, planned files, открытые вопросы и corpus baseline.
- Scope/Evidence pass: оба симптома связаны с общим helper; read-only примеры и 3944/5216-строчный corpus baseline подтверждают его дефект. Изменено только содержимое этой SPEC. Причина индивидуального случая по-прежнему не утверждается без конкретной пары.
- Contract pass: семь AC покрывают распознавание, целостность, реальный ввод, оба popup, наследование, фильтрацию, совместимость, save/reload и обязательную validation. Выбор ключей/данные/отношения не переопределяются. Распознавание корпусом и видимый glyph отделены.
- Adversarial risk pass: проверены prefix-порядок, обычный текст/цифры, modifiers/ZWJ, уцелевший носитель старого ключа, несколько родителей, до/после save, font/video limitations. Неподтверждённое усложнение matcher удалено после проверки counterexample.
- Role-Based pass: результаты в таблице выше; tester/UX/developer/delivery gates включены в AC и план, фактический GREEN не выдуман.
- Отдельный reviewer: `/root/emoji_spec_review` выполнил adversarial read-only работу и не менял файлы. Effective sandbox — `danger-full-access`, filesystem unrestricted: это НЕ технически независимый `read-only` sandbox. По fallback review owner основной агент выполнил отдельную проверку фактов и релевантный re-review. Остаточное ограничение — отсутствие принудительно read-only reviewer; оно не подменяется названием роли.
- Evidence inspected: `EmojiTextHelper.cs:12–58`; `TaskItemViewModel.cs:192–199,438–457,641–642,819–861`; `MainWindowViewModel.cs:825–879,902–951`; `EmojiTextBlock.cs`; `MainControl.axaml` parent-trail binding; UI-тест карточки `CurrentTaskCard_ParentEmojiTrail_RefreshesWhenAncestorTitleChangesBeforeSave`, существующий filter screenshot harness; официальные Unicode17 данные и current runner/CI. `ffmpeg`/`ffprobe` и interactive session доступны.
- Fix and re-review: финальная схема оставляет один regex, corpus-first longest-first и прежний fallback; const API согласован с helper. Контрольный old regex дал для `😀` + U+FE0F match length 2 при input length 3; формы `👭🏻`/`👬🏻`/`👫🏻` есть в Unicode17, поэтому тоже не обосновывают усложнение. Перепроверены §6.2, §7, AC2/matrix, alternatives, linter и role verdict; ложное основание не используется. Конкретизированы persistence-класс/команда и детерминированная autosave-проверка.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | evidence/design | В промежуточный текст попал неподтверждённый legacy VS16-контрпример и усложнённый matcher | Проверить Match.Length/ordinal, удалить ложное утверждение и лишний механизм | fixed |
| MEDIUM | validation | Первичный план не называл save/reload-класс и конкретный способ доказать «до автосохранения» | Назвать класс/команду, детерминированно управлять fixture saves и сохранять UI evidence | fixed |

Depth checklist:

| Область | Результат инспекции |
| --- | --- |
| Scope drift / unrelated changes | Агент изменил только текущую SPEC, production diff отсутствует. На финальном status дополнительно появился unrelated `specs/2026-10-02-task-card-status-recovery.md`; он не читался, не менялся и не относится к этому change set. Новое дерево/данные/релиз вне scope |
| Acceptance criteria | Все семь AC имеют automated/visual evidence plan, полный shared-helper regression набор |
| Scenarios / decisions / objections | Обе исходные поверхности, повторения, save/reload; ключевая семантика сохраняется, диагностический вопрос необязательный |
| Validation evidence | Baseline — read-only regex; UI/build/test GREEN пока нет; commands и artifacts определены |
| Unsupported claims | Индивидуальная причина/полная glyph-поддержка/невозможность видео не заявлены; ложный counterexample отозван |
| Regression / edge risk | Prefix boundaries, old forms, plain text, duplicated keys, multiple parents, repeat changes защищены планом |
| Comments/docs/changelog | Только spec; generated источник/версия и пояснение fallback актуальны; changelog/releases вне scope |
| Hidden API/UX/operations change | Const остаётся string, расширяется набор; helper и direct regex согласованы; selection semantics прежние |
| Manual-review challenge | Не сможет ли UI-тест пройти без фактического нового пункта/видимого trail? AC требует controls и просмотр кадра. Не заменён ли исходный симптом абстрактным parser test? Есть TextBox/потомки/filter assertions |

- No-findings justification после исправлений: открытых actionable находок нет; сопоставлены источники, output contract, семь AC, весь planned write set и ограничения evidence. Проверки EXEC остаются работой, а не выполненным результатом.
- Stop decision: закончить SPEC, дождаться «Спеку подтверждаю». Никакие тесты/сборки, создающие артефакты вне SPEC, до approval не запускались. Конкретный пример пользователя, если поступит, включить в regression.

### Post-EXEC Review — исторический результат до устранения blocker

Этот подраздел фиксирует предыдущий NEEDS-FIX, а не текущий итог. Таблицы findings, depth checklist и failure audit ниже сохранены как история проверки. Актуальные disposition и stop decision находятся в «Финальный Post-EXEC Review точной пары, 04.10.2026».

- Статус: **NEEDS-FIX**. Подтверждённый дефект распознавателя исправлен локально; обязательный AC7 пока не выполнен. Незелёный полный main не подменяется targeted GREEN и не оформляется как `accepted-risk`.
- Scope reviewed: утверждённая SPEC, `git status --short`, `git diff --stat`, relevant tracked diff и содержимое новых helper/data/generator/UI-builder/persistence/license файлов; profile/UI override, TUnit evidence, source/asset атрибуция и отсутствие изменений подписок, JSON, отношений и storage/server production-кода.
- Scope/Evidence pass: production change set — corpus-first union и generated UTF-16 trie, неизменённый legacy fallback, полный Unicode notice в output/publish. Test change set — corpus/negative/long-input, save/reload, ввод Title, оба popup, ребёнок/внук и видимые деревья. `specs/2026-10-02-task-card-status-recovery.md` отделён как unrelated: не читался и не изменялся.
- Contract pass: AC1–6 подтверждены helper/persistence и последним `final-reviewed` targeted run 3/3 с просмотренными кадрами. Semantics выбора старого ключа, нескольких эмодзи и include/exclude не меняются. AC7 требует именно зелёных обязательных проектов, поэтому full-main failures блокируют completion.
- Adversarial risk pass: сверены longest-prefix, tag/ZWJ/VS/modifiers, standalone legacy components, обычный текст/цифры/непарные суррогаты, длинный input, повторное удаление/добавление, другой носитель старого ключа, сохранение независимого выбора, несколько родителей и до/после save. Неизменность legacy literal проверена ordinal относительно HEAD (14864 UTF-16 code units). Не обнаружено нового production counterexample; это не доказательство всех будущих Unicode/glyph форм.
- Role-Based pass: результаты в таблице ниже. Отдельный `/root/emoji_spec_review` работал без мутаций; его effective sandbox `danger-full-access` не даёт технически независимого read-only review. Основной агент отдельно проверил contract, raw logs, diff, viewed frames и adversarial cases; отсутствие enforced read-only reviewer записано как ограничение. Финальный reviewer просмотрел все 27 `final-reviewed` PNG, закрыл visual/provenance findings и подтвердил NEEDS-FIX только по AC7.
- Fix and re-review: после замечаний добавлены четыре popup-перехода, recursive projected IDs и реальные видимые TreeViewItem; устранено преждевременное чтение визуального дерева через явный layout/ForceRenderTimerTick и ожидание элемента. Последний pass дополнительно потребовал focus списка перед Escape, assertion закрытия popup на каждом шаге и disk-seed начальных Title карточки с assertion 🧭/🛠 перед before-кадром. Все изменения проверены последним `green/final-reviewed/ui.log`: 3/3, 45s358ms. Новые before/empty-selected/phoenix-grandchild кадры просмотрены и согласованы; popup закрыт в обоих empty-selected кадрах, исходный trail содержит 🧭/🛠. Notice source/desktop output/candidate-final output имеют одинаковый SHA-256 `E7A93B009565CFCE55919A381437AC4DB883E9DA2126FA28B91D12732BC53D96`.
- Stop decision: **NEEDS-FIX по обязательной validation**, EXEC не закрыт. Не изменять unrelated production-функции ради зелёного отчёта и не заявлять Git delivery, установку, CI или индивидуальный пользовательский сценарий проверенными.

| Роль | Результат post-EXEC |
| --- | --- |
| Business analyst / domain workflow | Новая бизнес-логика не вводится; прежняя семантика группировки по ключу и фильтрации проверена в UX/contract pass |
| UX / designer | Просмотренные final-reviewed before/after кадры показывают новый пункт и trail, include/exclude selected frames показывают нужный состав дерева и закрытый popup; исходный trail согласован |
| Tester / validation | RED воспроизводит обе поверхности, корпус проверен без сети, before-save доказан неизменным файлом, save/reload — отдельным тестом. Полный main не зелёный; AC7 остаётся blocker |
| Developer / architect | Один compiled regex, public const string API сохранён; corpus-first greedy trie и старый fallback согласованы с прямым Regex и всеми helper methods. Нет новой runtime dependency/миграции |
| Delivery / operations / security | Локальные synthetic fixtures, notice включён в output/publish; raw corpus защищён `.gitattributes -text`. Publish/установка/личные данные не затрагивались |

Evidence inspected:

| Evidence | Подтверждённый результат |
| --- | --- |
| `red/helper.log`, `red/ui-filter-diagnostic.log`, `red/ui-rendered.log` | Старый matcher: 10/23 failed helper tests; 635/5216 combined-contract failures; 🫶 отсутствует в двух popup и trail ребёнка |
| `green/helper.log`, `green/persistence.log` | 23/23 и 1/1; весь корпус 5216/5216, raw hash и direct regex, сохранение/перезагрузка Title/ID/relations |
| `green/final-reviewed/ui.log`, PNG той же папки | Последний run 3/3, 45s358ms; просмотрены согласованный card-before, empty-selected с закрытым popup и phoenix-grandchild. Первоначальные popup/child-selection кадры также сверены |
| `green/full-headless-final.log` | Полный Headless 51/51, 0 failed/skipped, 11m07.569; отменённый первый partial run не засчитан |
| `green/desktop-final-build.log` | Стандартная desktop build: 0 errors, 5 warnings; полный notice действительно скопирован, hash совпадает |
| `green/full-main.log` | Полный основной набор завершён: 1174 total, 1164 passed, 10 failed, 0 skipped; exit 2, 1h06m49.571. Binary до последних harness fixes, production recognizer тот же |
| `baseline-*.log`, `current-*.log` | Hydration/TreeDelete одинаково падают на baseline/candidate. Server isolated совпадает, его full symptom отличается. TreeSearch 7/7, CLI legacy-dates 7/7 и title-save 1/1 проходят на обоих вариантах |
| Generator, `.gitattributes`, checksum/legacy ordinal checks, `git diff --check` | Детерминированная генерация; raw corpus hash сохранён; старое выражение неизменно; whitespace errors отсутствуют |

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | UI coverage | Первичные assertions не доказывали descendant membership/повторные popup-переходы | Проверить 4 перехода, projected IDs и видимые TreeViewItem в include/exclude | fixed; targeted 3/3 |
| MEDIUM | visual evidence | Преждевременное чтение отрисованных элементов и непригодные старые кадры | Явный layout/render wait, свежий test output и PNG | fixed; свежие final-targeted кадры просмотрены, старые root-green PNG не засчитываются |
| MEDIUM | visual evidence | Empty-emoji selected кадр оставлял popup; исходная карточка снималась с несогласованным trail | Focus+Escape+closed assertion; seed до Connect и before-trail assertions | fixed; final-reviewed 3/3 и новые кадры просмотрены |
| MEDIUM | provenance | Полный notice первоначально копировался только тестам | Включить notice в стандартные output/publish assets | fixed; desktop output hash совпадает с source |
| MEDIUM | validation | Graph-command wrapper прошёл baseline, но упал candidate на другом шаге, чем full run | Сохранить различие и исследовать completion/timing отдельно; не приписывать ему неподтверждённую причину | open внутри AC7; unresolved validation, не accepted-risk |
| BLOCKER | validation / AC7 | Полный обязательный main suite 1164/1174, 10 failed | Сохранить failure audit и разрешить препятствия в соответствующем scope; не объявлять PASS | open; NEEDS-FIX; два старых harness failures закрыты финальным UI run, восемь остальных остаются failure evidence общего набора |

Depth checklist:

| Область | Результат |
| --- | --- |
| Scope drift / unrelated changes | Изменения связаны с общим parser, его data/provenance и regression evidence; unrelated status SPEC сохранена. Server/storage/CLI production не изменялись |
| Acceptance criteria | AC1–6 имеют конкретные автоматические проверки; последние UI evidence fixes проверены run 3/3 и кадрами. AC7 не выполнен и не ослаблен |
| Scenarios / matrix / objections | Оба исходных симптома, выбор нового ключа, 4 перехода, два предка и save/reload сверены с matrix. Точная пара пользователя не получена; диагноз индивидуального случая остаётся гипотезой |
| Validation evidence | RED/GREEN, standard desktop и полный Headless подтверждены; полный main/paired baseline честно отделены от successful targeted runs |
| Unsupported claims | Нет заявления о всех будущих glyph, ускорении, установленном приложении, CI или публикации; isolated pass не называется исправлением причины full-run failure |
| Regression / edge cases | Составные/legacy/negative/длинные данные, multiple-parent traversal, независимый выбор и неизменность ID/relations проверены |
| Comments/docs/changelog | Generated version/hash/license/generator pointer актуальны; SPEC и journal обновляются. Release changelog вне текущего delivery scope |
| Hidden API/UX/operations contract | Const остаётся string; распознаваемый набор расширен явно. Автоматического переноса выбора фильтра нет; JSON/статусы/отношения прежние. Notice — asset, не runtime dependency |
| Manual-review challenge | Визуальная проверка действительно нашла слабые кадры и привела к исправлениям harness; assertions VM не подменяют pixels. Baseline failures не скрываются словом flaky и не превращают AC7 в PASS |

- No-findings justification: формулировка «Нет находок» для всей фазы неприменима — есть открытый обязательный validation blocker. Дополнительных production correctness находок после adversarial/fallback review нет в проверенных границах корпуса и сценариев.
- Needs human: для исправления parser продуктовых решений не осталось. Существенное расширение на unrelated storage/server/UI-command bugs требует отдельного scope/SPEC; текущая approval не даёт права менять эти контракты незаметно.
- Residual risks: полный main не зелёный; точная индивидуальная пара не проверена; шрифт новых версий не обновлялся; technically read-only reviewer недоступен. Эти ограничения не маскируются итоговым PASS.

### Failure audit полного main — исторический snapshot

`green/full-main.log`: 1174 total, 1164 passed, 10 failed, 0 skipped, exit 2. Полный run запускался с `--maximum-parallel-tests 1 --output Detailed`; он содержит предыдущую версию новых UI-тестов. Последний binary отдельно собран в `candidate-final`, targeted run — `green/final-reviewed/ui.log`. Полный latest suite не объявляется зелёным по совокупности отдельных rerun.

Baseline — неизменённый `git archive` HEAD `46711e60d6ef453e794104ee7d9f81ad6f37c68c` в `artifacts/emoji-title-refresh/baseline-source`; production source не исправлялся. Перед финальным audit live HEAD и write set повторно сверены: база прежняя, tracked diff только в пяти перечисленных файлах; новые файлы перечислены в §16. Paired runs используют тот же SDK/Debug и точный `--treenode-filter "/*/*/<class>/<method>"`, serial execution. Во время длинной проверки наблюдались другие test processes в отдельных worktrees; это возможный environment фактор, а не установленная причина failures. Они не останавливались.

| Failed cases (число) | Full-run symptom | Paired / final evidence | Disposition |
| --- | --- | --- | --- |
| `Toolbar_EmojiFilters_TitleInputCreatesNewFilterAndPreservesOtherSelections` false/true (2) | 🫶 ещё не найден в визуальных элементах | Latest final-reviewed 3/3 с ожиданием render/layout, visible tree assertions и 27 просмотренными кадрами | Исправлен test harness; fresh affected run зелёный |
| `DisabledWatcherIdentityRaisedDuringCacheHydration_IsDiscarded` (1) | First cache hydration batch timeout | `baseline-hydration.log` и `current-hydration.log`: одинаковый timeout | Воспроизведён на исходном коде; AC7 blocker не снимается |
| `ServerStorageCrudRealtimeScenario_ExecutesFeatureSteps` (1) | Owner page не содержит только что созданный ID | `baseline-server.log` и `current-server.log`: оба падают на GetTask с `Sequence contains no elements`, другой symptom | Baseline instability сценария доказана; конкретный full-run symptom не подтверждён baseline, причина не установлена |
| `TreeCommandUi_ShiftDelete_RemovesSelectedLastUpdatedTreeItem` (1) | Файл Task4 остаётся | `baseline-tree-delete.log`, `current-tree-delete.log`: тот же `IsNull` failure | Воспроизведён на исходном коде; исправление команд удаления вне текущего production write set |
| `TreeSearch_ClearSearch_RestoresExpansionState(LastCreatedTree)` (1) | `parentFilteredOut` false | `baseline-tree-search.log` и `current-tree-search.log`: 7/7 на обоих | Full-run причина не воспроизведена; isolated pass не считается её исправлением |
| `Apply_LegacyInvalidDatesDoNotBlockIndependentChanges(legacy-title)` (1) | CLI process timeout | `baseline-cli-legacy-dates.log` и `current-cli-legacy-dates.log`: 7/7 на обоих | Full-run причина не воспроизведена; CLI не использует изменяемый ViewModel/parser |
| `NewTask_TitleNotResetAfterFileSave` (1) | `savedWithoutReset` false | `baseline-title-save.log` и `current-title-save.log`: 1/1 на обоих | Full-run причина не воспроизведена; ordinary ASCII Title, семантическая связь с расширением emoji-набора не установлена |
| `TaskGraphWorkspaceCommandScenario_ExecutesFeatureSteps` (1) | Main-tree ShiftDelete оставляет Task4 | `baseline-graph-command-story.log`: 1/1; `current-graph-command-story.log`: 0/1, теперь другой failure — task count 26 вместо 24 в `CurrentTaskItemRemove_Success` | Открытая validation неопределённость: нельзя утверждать ни baseline-origin данного failure, ни доказанную regression parser. Оба напрямую вызываемых underlying tests прошли в full run; wrapper не зелёный |
| `WorkspaceTreeCommandsScenario_ExecutesFeatureSteps` (1) | `PasteOutlineCommandWorked` false | `baseline-tree-command-story.log`, `current-tree-command-story.log`: 1/1 на обоих | Full-run причина не воспроизведена |

Graph timing-гипотеза имеет проверяемое основание, но не считается установленной причиной. `RemoveTaskItem` в `MainWindowViewModel.cs` передаёт async lambda в `ManagerWrapper.Ask(Action)`; mock вызывает Action без ожидания, поэтому callback удаления — async void. `TestHelpers.ActionNotCreateItems` ждёт throttle и pending saves, а не completion удаления. В данном fixture это 10ms throttle + 100ms; expected24/actual26 при `changeCount=-2` означает, что исходные 26 задач ещё наблюдаются, а не что созданы две дополнительные задачи. Эти источники одинаковы baseline/current; исходные Title — ASCII, оба underlying удаления прошли как самостоятельные cases в full run. Это согласуется с неполным ожиданием, но baseline не воспроизвёл данный candidate-only symptom; возможное влияние parser на тайминги не измерено. Основной агент прочитал RemoveTaskItem/mock/ActionNotCreateItems, reviewer выполнил отдельный adversarial disposition; source не менялся.

Ошибочный OR-filter попытался выбрать методы, но запустил 0 tests (exit 8): `baseline-additional-failures.log`, `current-mixed-failures.log`. Он не засчитан как validation. После чтения summary переход выполнен на точные filters по одному методу, результаты выше. Discovery без executing tests показал 1174 cases, но сам по себе не использовался как pass.

На этом этапе оставшиеся failures не подавлялись и unrelated production-подписки/хранилище/CLI не изменялись. Обязательный validation blocker тогда оставался открытым даже при доказанном baseline failure: stop decision был NEEDS-FIX. После поручения «Исправь» обновлены тестовые ожидания и выполнены свежие полные наборы; их итог зафиксирован ниже.

Video fallback проверен предметно: `EmojiTitleTestAppBuilder` использует Avalonia.Headless с реальным Skia renderer. В [HeadlessWindowImpl версии 12.0.3](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.0.3/src/Headless/Avalonia.Headless/HeadlessWindowImpl.cs) `Handle` равен `PlatformHandle(IntPtr.Zero, "STUB")`; Win32-окна у этого test run нет. Доступный `record_app_window.ps1` перечисляет Win32 HWND, вызывает `GetWindowRect` и пишет через `gdigrab`; `record-status-contract-evidence.ps1` ожидает native window-ready handshake существующего другого FlaUI-сценария. Поэтому эти recorders не могут записать данный Headless regression run. `ffmpeg`/`ffprobe` и interactive desktop доступны; ограничение относится к runner, а не к отсутствию инструмента. Проверка: `rg -n 'HWND|WindowTitle|gdigrab|GetWindowRect' C:/Users/Kibnet/.codex/skills/record-app-screen/scripts -g '*.ps1'`, осмотр builder и соответствующего исходника Avalonia. Next-best evidence — реальные до/после PNG `CaptureRenderedFrame()` из тех же автоматизированных тестов, RED/GREEN-логи и assertions видимых controls; видео другого сценария не засчитывается.

### Исторический финальный Post-EXEC Review, 03.10.2026

- Статус: **PASS**. AC1–7 выполнены локально. Обязательный validation blocker закрыт свежими полными прогонами последнего source/test change set; прежние failures и незавершённые диагностические запуски сохранены выше и в raw logs.
- Scope reviewed: утверждённая SPEC и дополнительное поручение «Исправь», §11 matrix, §16 write set, User-Observable Scenarios, Decision Ledger и Expected User Review Objections; финальные `git status --short`, `git diff --stat`, relevant source/test diff, новые data/generator/helper/persistence/builder/license файлы, central review/testing owners и local UI override. Изменения docs ограничены этой SPEC, атрибуцией generated data и лицензией; release changelog не нужен для локального результата.
- Scope/Evidence pass: production diff остаётся общим corpus-first распознавателем, неизменным legacy fallback и notice asset. Remediation затрагивает только тестовые ожидания, fixtures и UI harness. Финальный write set из 18 source/data файлов с SHA-256 и временем изменения сохранён в `remediation/final-source-hashes.json`; после `build-popup-final.log` source/test edits не было. Unrelated `specs/2026-10-02-task-card-status-recovery.md` не читался и не менялся.
- Contract pass: AC1/2 закрыты офлайн-корпусом из 5216 форм, точными Extract/Remove/Split/direct Regex assertions, отрицательными и длинными входами; AC3/4/5 — вводом в реальный TextBox, четырьмя переходами, include/exclude selection и видимыми TreeViewItem/parent trail ребёнка и внука; AC6 — точным read-back Title/ID/relations после save/reload. AC7 закрыт стандартной desktop build, полными main/Headless и просмотром последних PNG. Выбор старого ключа не переносится на новый, несколько эмодзи сохраняют общий ключ, независимый выбор и другой носитель старого ключа сохраняются; Non-Goals соблюдены.
- Adversarial risk pass: повторно сопоставлены корпус/legacy boundaries, составные ZWJ, VS, tag, keycap, flags и modifiers, обычный текст/цифры/непарные суррогаты, долгий input, несколько предков и повторные удаления/добавления. Для UI оспорена возможность ложного GREEN через VM-only assertions или подготовку popup прямой мутацией: остаются реальные Title input, visible text, keyboard open/close с проверкой prerequisites и Space selection; direct `Popup.IsOpen` assignment не используется. Существующий mouse flow покрывается неизменёнными тестами, прошедшими в том же full main. Подписи, поиск и остальные потребители helper дополнительно прошли полный набор.
- Role-Based pass: применимые роли и конкретные результаты перечислены ниже. `/root/emoji_spec_review` выполнил отдельную инспекцию без мутаций и просмотрел все последние 27 PNG. Его effective sandbox — writable `danger-full-access`, поэтому технически независимым read-only review этот pass не считается. Основной агент выполнил adversarial fallback: проверил source contracts, raw summaries, diff, hash/provenance и include/exclude/grandchild кадры; ограничение sandbox сохраняется явно.
- Fix and re-review: прежние UI evidence/provenance findings закрыты более сильными control assertions, disk seed, render/dispatcher waits и полным notice в production output/publish. Remediation ожидает завершение async удаления в cache/disk, освобождает worker в hydration pump, ждёт обе non-stale Raven query shapes перед прежними authenticated HTTP assertions и заменяет три blocking search waits на async polling с прежними predicates/15s bounds. Для двух новых toolbar regressions последующий full установил закрытый popup как невыполненный prerequisite; normal focus+Enter/Escape flow и open/closed assertions проверены 3/3 с capture и без него, затем в fresh full. Assertions ID, файлов, количества, прав доступа, UI membership, raw Title и before-save состояния сохранены; ошибки не подавлялись, прежние сроки без диагностики не увеличивались.
- Reviewer re-review: после завершения Headless отдельный reviewer повторно прочитал обе полные summaries и актуальный раздел этой SPEC, сверил все 18 source/data hashes и notice source/output, закрытие старых findings и границы claims. Вердикт — **PASS, открытых BLOCKER/HIGH/MEDIUM/LOW findings нет**; чтения без мутаций и новых тестовых запусков. Writable sandbox ограничение сохраняется.
- Stop decision: **PASS; локальный EXEC завершён**. Новый полный rerun после зелёных обязательных наборов не требуется: source не менялся, новые незакрытые обязательные findings отсутствуют. Git delivery, CI, установка и запуск пользовательского installed app не являются результатом этой задачи.

| Роль | Финальный результат |
| --- | --- |
| Business analyst / domain workflow | Прежняя группировка по точному emoji-ключу сохранена. Общий подтверждённый parser defect воспроизводит оба исходных симптома; новая логика статусов/отношений не вводится |
| UX / designer | Последние 27 PNG из `visual-popup-final` просмотрены: новые popup-пункты, before/after trail, include с parent+child и exclude без них, empty-selected с закрытым popup. Keyboard route допустим обычным control contract; mouse-проверки также прошли |
| Tester / validation | RED обеих поверхностей сохранён; corpus/persistence и UI on/off GREEN. Fresh full main 1174/1174 и full Headless 51/51 без failed/skipped; все первоначальные main failed cases найдены как passed |
| Developer / architect | Один compiled regex, публичный const string и методы helper сохранены; новый corpus-first trie и прежний fallback согласованы. Нет runtime dependency, network lookup, schema migration или production server/storage/CLI diff |
| Delivery / operations / security | Только synthetic fixtures и локальные artifacts; полный Unicode notice копируется в стандартный output. Source и desktop asset hash повторно совпали; публикация/установка не выполнялись |

Evidence inspected (пути логов ниже относительно `artifacts/emoji-title-refresh/`):

| Evidence | Финальный подтверждённый результат |
| --- | --- |
| `red/helper.log`, `red/ui-filter-diagnostic.log`, `red/ui-rendered.log` | Исходный matcher нарушает корпусный контракт и не показывает 🫶 в двух popup и trail ребёнка; baseline RED не заменён предположением |
| `green/helper.log`, `green/persistence.log` и соответствующие cases в final main | 23/23 helper и 1/1 persistence; 5216 форм; raw Title/ID/relations после save/reload |
| `remediation/build-popup-final.log` | Последний стандартный test build: 0 errors, 57 warnings, 23.04s; source после этого build не менялся |
| `remediation/ui-popup-no-capture-final.log`, `ui-popup-capture-final.log` | Те же три UI cases: 3/3 без capture (14.503s), 3/3 с capture (26.845s) |
| `remediation/visual-popup-final/*.png` | Все 27 просмотрены reviewer; основной агент отдельно просмотрел include/exclude phoenix selected и grandchild-after |
| `remediation/full-main-final.log` | Полный unfiltered `Unlimotion.Test`: 1174 total, 1174 succeeded, 0 failed/skipped, 14m37.158s, exit 0. Включает обе новые toolbar cases, card trail и все прежние failing cases; unchanged mouse/AllToggle проверки также прошли |
| `remediation/full-headless-final.log` | Полный unfiltered `Unlimotion.UiTests.Headless`: 51 total, 51 succeeded, 0 failed/skipped, 2m08.701s, exit 0 |
| `remediation/desktop-build.log` | Обычный desktop build: 0 errors/5 warnings, 3.38s. Production write set после него не менялся; последующие исправления относятся только к тестам |
| `remediation/final-source-hashes.json`, source/output notice, `git diff --check` | Corpus SHA-256 `1D8A944F88D7952F7EF7C5167FEF3C67995BCAE24543949710231B03A201ACDA`; notice source и `src/Unlimotion.Desktop/bin/Debug/net10.0/licenses/Unicode-Emoji.txt` имеют SHA-256 `E7A93B009565CFCE55919A381437AC4DB883E9DA2126FA28B91D12732BC53D96`. Whitespace errors отсутствуют |

Финальные полные команды: `dotnet run --no-build --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --maximum-parallel-tests 1 --output Detailed`, затем `dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed`. Это два последовательных завершённых запуска; partial/filtered диагностические логи не объединялись в итоговый pass.

| Severity | Area | Finding | Required action | Финальный disposition |
| --- | --- | --- | --- | --- |
| MEDIUM | UI coverage / visual evidence | Ранние assertions/кадры не доказывали повторные переходы и descendant membership | Проверить реальные controls, четыре перехода, видимые дочерние узлы и корректные before/selected кадры | fixed; latest UI 3/3 с capture и без, 27 PNG просмотрены, те же cases passed в final full |
| MEDIUM | provenance | Полный notice первоначально копировался только тестам | Включить notice в стандартные output/publish assets | fixed; source/desktop output hash совпал, production csproj содержит CopyToPublishDirectory; publish не заявляется выполненным |
| MEDIUM | validation / async completion | Graph/story count и deletion assertions могли читать незавершённый async результат; search ожидал синхронно | Ожидать проверяемый cache/disk/count результат, сохранить assertions и budgets | fixed как validation finding; targeted и fresh full соответствующие cases passed. Общая причина всех прежних intermittent failures не заявляется установленной |
| MEDIUM | UI harness prerequisite | Render-only fix не открывал popup; в failing diagnostic `popup=False`, listBounds0 при корректных bound данных | Использовать обычный keyboard open/close с assertions prerequisites и сохранить все visible/selection checks | fixed; latest UI on/off и final full passed. Причина прежнего mouse-click/geometry поведения не объявляется доказанной |
| BLOCKER | validation / AC7 | Предыдущий main был незелёным; targeted GREEN недостаточен | Выполнить оба обязательных полных проекта последнего change set | fixed; main 1174/1174, Headless 51/51, оба exit 0. AC7 не ослаблен и не заменён accepted-risk |

Depth checklist:

| Область | Проверяемый итог |
| --- | --- |
| Scope drift / unrelated changes | §16 и финальный status совпадают с текущим write set; status-recovery SPEC отделена и сохранена; production сервер/хранилище/CLI не менялись |
| Acceptance criteria / scenarios / matrix / objections | AC1–7 закрыты конкретными evidence выше. Новая emoji доступна до save, выбор меняет реальное дерево, цепочка нескольких родителей обновляется, исходные данные сохраняются. Missing exact user pair не заменён заявлением об индивидуальном диагнозе |
| Validation evidence | Полные завершённые mandatory runs последнего binary зелёные; старые failures/partial runs сохранены как история; targeted и visual evidence дополняют full, а не заменяют его |
| Unsupported claims | Не заявляются установленная причина всех старых intermittent failures, влияние capture, причина optional filtered stall, поддержка будущих glyph, ускорение, CI, publication или installed-app проверка |
| Regression / edge cases | Corpus/legacy/negative/long-input, several-parent traversal, repeat edits, независимый selection, другой носитель старого ключа и точные ID/relations проверены |
| Comments/docs/changelog | Generated источник/версия/hash/generator pointer и notice согласованы; SPEC отражает последний итог и исторические failures. Release notes вне порученного scope |
| Hidden API/UX/operations contract | Public const остаётся string, распознаваемый набор расширен явно; перенос выбора, JSON normalization и новые status/storage contracts не введены. Test waits не подавляют fail и не заменяют исходные assertions |
| Manual-review challenge | Проверен риск красивого PNG без выбора/реального дерева: assertions и selected кадры согласованы. Keyboard flow не подменяет production behavior прямой state mutation; обычное mouse coverage осталось и passed. Latest full воспроизводит фактический последующий порядок остальных тестов |

- No-findings justification: после Fix and re-review открытых actionable correctness/обязательных validation findings нет. Сопоставлены реальный diff, все AC, пользовательские сценарии, visual evidence, публичный parser contract, точный persistence и полные mandatory summaries; прежние findings закрыты по evidence, не по предположению о flaky-тестах.
- Needs human: продуктовых решений или нового разрешения для завершения локального scope не требуется.
- Residual risks / follow-ups: точная пара эмодзи исходного сообщения не предоставлена; шрифт не обновлялся, будущая glyph-поддержка не обещается. Reviewer sandbox технически writable; применён отдельный adversarial fallback. Optional filtered whole-toolbar run остановился на pending headless dispatch, причина не установлена и не объявляется устранённой; обе соответствующие unchanged AllToggle cases затем прошли в обязательном полном run. Эти границы не нарушают выполненные AC; видео заменено предметно обоснованными PNG по описанному выше Headless fallback.

## Approval

Получена фраза «Спеку подтверждаю» 02.10.2026. Она разрешает локальную реализацию и проверки описанного scope, отдельно не разрешает Git delivery, установку или публикацию.

### Устранение validation blocker, 03.10.2026

Пользователь поручил «Исправь» после результата NEEDS-FIX. Продолжается EXEC: необходимо устранить оставшиеся препятствия AC7 и выполнить свежие обязательные наборы. Дополнительный write set ограничивается тестовыми fixtures/helpers и затронутыми UI-тестами (`FileStorageTaskStatusTests`, `ServerStorageCrudRealtimeContract`, `TestHelpers`, `MainControlTreeCommandsUiTests`; при установленной причине также `MainWindowViewModelTests`/`WorkspaceTreeCommandsUiContract`). Product scope, требования AC7 и серверные/CLI/storage production-контракты сохраняются. Ожидания должны проверять завершённый результат; исходные проверки ID, доступа, файлов, количества и UI-проекций сохраняются. Таймауты без диагностики не увеличиваются, падения не подавляются. Для симптомов, которые не воспроизведены изолированно, сначала исследуется механизм, затем свежий полный run; единичный pass не считается доказательством исправления.

Fresh full выявил два remaining failure новых toolbar regressions на visible TextBlock predicate (данные bound list уже корректны). Isolated run без capture также2/2, поэтому влияние capture остаётся гипотезой. Узкая правка в ранее утверждённом `MainControlFilterToolbarResponsiveUiTests`: async await + явные чередующиеся dispatcher/render ticks по [реальному HeadlessWindowExtensions12.0.3](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.0.3/src/Headless/Avalonia.Headless/HeadlessWindowExtensions.cs); predicate и2s deadline сохраняются, failure включает transition/popup/layout snapshots. Production matcher и UI-контракты прежние. Проверить одинаковые три UI cases с capture on/off и получить новый full mandatory run.

Следующий full diagnostic локализовал failure: transition1, boundlist содержит🫶, но `popup=False`, listBounds0. Поэтому render-only исправление не закрывает prerequisite. В регрессии явно focus+Escape+closed wait перед редактированием и focus+Enter+open wait после ввода: обычный keyboard flow, существующий в контроле; direct `Popup.IsOpen` mutation отсутствует. Проверки actual TextBox ввода, visible popup text, выбор нового ключа черезSpace, потомки, четыре перехода и unchanged disk сохраняются. Требуется новый fresh full; один targeted pass не доказывает устранение full-run симптома.

### Доставка на GitHub, 04.10.2026

Пользователь поручил «Оформи PR». Разрешены рабочая ветка `fix/emoji-title-refresh`, коммит, push в origin и PR в `main`. Это отдельное разрешение поверх завершённого локального EXEC; merge, release и установка не поручены. Preflight подтвердил GitHub repository `Kibnet/Unlimotion`, authenticated account, отсутствие существующего PR этой ветки и совпадение обновлённого `origin/main` с проверенной базой `46711e60d6ef453e794104ee7d9f81ad6f37c68c`. Все 18 source/data SHA-256 совпадают с финальным manifest; нового изменения поведения нет, повторные полные тесты не требуются. В коммит входят только §16 write set и эта SPEC; unrelated status-recovery SPEC остаётся вне коммита. Локальные ignored logs/PNG перечисляются в PR как local-only evidence, а не как опубликованные assets.

### Уточнение исходного случая, 04.10.2026

Получена точная пара `🧙‍♂️ → 🪼`. Прямое ordinal-сравнение старого и исправленного matcher подтвердило исходный дефект; `🪼` есть в зафиксированном корпусе как E15.0, `🧙‍♂️` — E5.0. В ранее утверждённых UI/persistence классах выделены общие scenario helpers, исходные три UI cases и persistence case сохранены, добавлены отдельные `ExistingTaskWizardToJellyfish` cases: оба popup, actual Title TextBox, видимое дерево, ребёнок/внук, несколько родителей, повторения и before-save disk assertion. В helper exact-sequence cases добавлены оба символа. Production helper/data/subscriptions не меняются; это уточнение утверждённого AC1–6, а не новое продуктовое решение. `jellyfish/green-exact.log` — 4/4, 0 failed/skipped, 45.712s; все 27 `visual-green` PNG просмотрены reviewer, основной агент отдельно проверил include/exclude selected и grandchild-after. Helper — 25/25, desktop — 0 errors/4 warnings. Source/visual re-review PASS; до завершения новых full runs AC7 последнего test change set остаётся pending, прежние 1174/1174 и 51/51 относятся предыдущей редакции tests. После проверки обновить PR #315 в ранее разрешённом Git delivery scope.

### Повторная обязательная проверка exact-pair coverage, 04.10.2026

Первый повторный `jellyfish/full-main.log` содержит два validation failures: `ServerStorageCrudRealtimeScenario_ExecutesFeatureSteps` — timeout Raven non-stale index wait (10927ms при10s), `DisabledWatcherIdentityRaisedDuringCacheHydration_IsDiscarded` — timeout до первой пачки (`Init=WaitingForActivation`, tasks0). На момент этого run fixture source-файлы не менялись после предыдущего опубликованного PASS. Точный wizard-to-jellyfish persistence, оба toolbar cases и card case прошли и в этом прогоне; это не закрывает AC7. После выявления узкого fixture rework собственный run остановлен с проверкой PID/executable/parent/arguments; partial не является завершённым full validation. Копия сохранена как `full-main-before-fixture-seed.log`. Snapshot компьютера показывает CPU100% и несколько одновременно работающих test processes из других worktrees; причинная связь с timeout пока гипотеза. Чужие процессы не изменяются, deadline не увеличивается для маскировки результата.

CI опубликованного `146d5f12` завершился SUCCESS для All tests, Android и CodeQL. Этот CI относится предыдущей редакции tests. Для доставки новой exact-pair coverage допускается draft PR с честным pending/failed validation; EXEC completion и ready требуют закрытия обязательного validation gate. Production parser, UI subscriptions и личный task space не менялись в данном уточнении.

Source-backed fixture rework: первоначальные65 TaskItem требовали status history, Version и availability/unlocked migration writes до первого cache yield. Они заменены теми же65 IDs/paths/Status.NotReady с актуальной Version, фиксированными CreatedDateTime/UnlockedDateTime и EnsureStatusHistory(); fake reports и pre-init отсутствуют. Exact64, disabled watcher identity, оба10s bounds и две event assertions сохранены. В Raven fixture две cold filtered query shapes подготовлены до HTTP insert, проверены Auto/ IndexName и !IsStale. После insert остаются отдельный session, Load(storedId), обе non-stale query с10s bounds, четыре HTTP requests и пять исходных authenticated assertions. Основание подготовки индекса: [Raven7.2 auto-index creation](https://docs.ravendb.net/7.2/indexes/creating-and-deploying?lang=csharp), [QueryStatistics](https://docs.ravendb.net/7.2/client-api/session/querying/how-to-get-query-statistics?lang=csharp). Это не доказательство устранения причин scheduling/I/O timeout.

Re-review source: PASS обеих fixture правок, новых HIGH/MEDIUM/LOW findings нет; reviewer не менял файлы, technical sandbox остаётся writable, основной агент отдельно проверил сохранение batch/event/auth/freshness predicates и отсутствие production drift. `build-fixture.log` —0 errors/97 warnings,2m12.39s; `hydration-seed.log` —1/1,3.255s; `server-prewarm.log` —1/1,59.237s. Hash manifest18 source/data files подтверждает: относительно первоначального полного PASS изменились только6 test source files, production/data drift0. Новый последовательный unfiltered main и Headless выполняется; gate pending до обоих завершённых summaries, targeted результаты не заменяют AC7.

Следующий полный binary с prewarm показал hydration PASS878ms, но серверный timeout повторился в первом postinsert query (10076ms, case1m45.820s). Prewarm не устранил симптом и убран из текущего candidate diff; попытка/targeted/history сохранены выше. В текущем source оба прежних query/predicates/10s bounds обёрнуты только failure-only диагностикой: GetIndexesStatisticsOperation и GetIndexingStatusOperation через установленный RavenDB.Client7.2.2, общий5s cancellation budget, snapshots в stderr, отдельное логирование недоступности диагностики и исходный throw. Happy path и пять auth assertions прежние. Running/Normal с lag без ошибок отличает незавершённый индекс от Paused/Disabled/Error, но сам по себе не устанавливает причину задержки. Текущий продолжающийся full относится binary с prewarm; новая диагностика ещё требует build/targeted и fresh mandatory validation.

### CI и следующий fixture/UI candidate, 04.10.2026

CI `37201696194` опубликованного `53d3795a` завершился1178/1180: оба card cases получили старый `trail.EmojiText` после успешного assertion нового `parent.Title`. Source показывает синхронное Title→RefreshDescendantEmojiFields, отдельно CurrentTaskItem/DataContext и ParentEmojiTrail UI bindings; немедленные20 RunJobs не доказывают завершение обеих очередей. Конкретная причина задержки в CI по одному логу не объявляется установленной. Test-only ожидание2s проверяет DataContext именно выбранного потомка, видимость, точный ожидаемый UI text и оба VM fields с dispatcher/render/dispatcher ticks; parent.Emoji проверяется отдельно, glyph/tools/ancestry/disk assertions сохранены, capture после readiness.

Новая hydration factory используется также для raw edits new/newer; добавлены StatusModelMigrationWasApplied=false,65 tasks после Init и availability report ChangedTasks=0. Подход сравнен с параллельным Importance candidate; его factory оставляет Version0, поэтому здесь сохранено явное FileTaskMigrator.Version. Личные task files не менялись.

Raven candidate сравнен с Importance: использует реальный GetAllTasks и возвращённую page в исходном owner assertion. Общий10s deadline установлен ДО первого запроса, каждый GetAsync.WaitAsync(remaining),50ms bounded poll только после успешной page без taskId. Ошибки HTTP/auth/server сразу fail. GetTask и все5 content/auth assertions остаются прямыми. Direct non-stale queries и неиспытанный failure-only diagnostic helper убраны; они не являлись требуемым HTTP результатом. Это source-backed test rework, а не доказательство устранения неизвестной причины всех server timeouts.

Старый run с prewarm остановлен после проверки собственной цепочки PID/executable/parent/arguments/shell command, чтобы собрать изменённый candidate. `full-main-with-prewarm-interrupted.log` — partial failed evidence, не полный итог; его Headless не выполнялся. Новые изменения не получают результаты старого binary. Source/adversarial re-review UI/hydration/API contract PASS; техническая read-only изоляция reviewer недоступна, применён root fallback. Build, targeted и оба full suite нового candidate обязательны.

Ветка синхронизирована с актуальным origin/main `49bc04e439c04d13958964e59f01cb864a9e33fa` отдельным merge commit `b513f6b5`;17 base-only файлов не пересеклись с собственными4 незакоммиченными файлами. Изменения PR #313 учитываются в следующем validation, его implementation не объявляется частью emoji fix. Unrelated status-recovery SPEC сохранена вне staging.

Targeted API candidate: server1/1 (62.738s), hydration class43/43 (12.206s), exact-pair4/4 (43.642s), original card1/1 (22.958s),27 новых PNG в `visual-api-readiness`. Следующая сборка с CTS:0 errors/54 warnings, server1/1 (37.055s), desktop0 errors/3 Git newline warnings. UI/hydration source в этой следующей сборке тот же. Это не полный AC7 PASS.

Fix and re-review выявил transport lifetime finding: WaitAsync ограничивает только caller, а Dispose JsonServiceClient10.0.6 ничего не отменяет. Token в SDK GetAsync помогает body read, но pending GetResponseAsync headers не получает token. Поэтому общий10s CTS передаётся GetAsync/Delay, OperationCanceledException только от budget преобразуется в TimeoutException, finally.Cancel защищает timer race; SDK client.Timeout перед каждым запросом ограничен remaining (сохраняет более короткий положительный configured timeout), SDK timer aborts pending request, original nullable Timeout восстановлен finally. Тип/захват Timeout и timer Abort проверены по фактическому ServiceStack.Client10.0.6/net10.0 и официальному source. Точный wall-clock callback при перегруженном threadpool не обещается. Нужны новый build/server и fresh full последнего source.

Последний Timeout candidate собран (`build-api-transport-final.log`:0 errors/54 warnings,39.69s), cold server1/1 (`server-api-transport-final.log`:44.064s). Все27 свежих PNG просмотрены reviewer, root отдельно проверил include/exclude selected и grandchild-after. Final source/contract/adversarial re-review PASS UI/hydration/API; transport MEDIUM закрыт source-backed SDK cancellation/timeout правкой. Техническая read-only изоляция по-прежнему недоступна. Hash manifest18 source/data (`final-source-hashes-api-transport.json`) отличается от первоначального полного PASS только в6 test files; собственные production/data changes относительно опубликованного matcher отсутствуют. Общий BLOCKER AC7 остаётся до fresh full main и Headless.

### Последующие CI и исправление phase/readiness контракта

Следующий published CI `37208175636` на91026572 завершился1179/1181: original card case опять содержит старый trail после ранее добавленного wait; exact wizard case не изменил parent.Title при вводе. Ожидание live-derived expectedTrail допускало согласованный старый state. Isolated exact4/original1 и27PNG не заменяют этот full failure. Локальный full91026572 также содержит server failure на первом HTTP GetAsync.WaitAsync10s; hydration/toolbar cases passed. После установленного contract rework собственный run остановлен с повторной проверкой PID/path/parent/shell marker; `full-main-api-ten-second-interrupted.log` — partial failed evidence, Headless данного pipeline не выполнялся.

Сопоставление исходного nullable SDK Timeout (фактический ServiceStack10.0.6 default60s) и закреплённого Raven7.2.2 source выявило ошибочное ужесточение: normal first dynamic query может ждать создание auto-index до15s. Поэтому десятисекундный consistency budget отделён от первого HTTP: natural GetAsync с исходным SDK timeout, затем при missing ID10s bounded poll с remaining SDK timeout/token. Источники: [QueryOverview7.2](https://docs.ravendb.net/7.2/querying/overview), [DynamicQueryRunner7.2.2](https://raw.githubusercontent.com/ravendb/ravendb/7.2.2/src/Raven.Server/Documents/Queries/Dynamic/DynamicQueryRunner.cs), [Index7.2.2](https://raw.githubusercontent.com/ravendb/ravendb/7.2.2/src/Raven.Server/Documents/Indexes/Index.cs). Это восстановление прежнего transport contract, не произвольное увеличение лимита и не доказательство конкретной CPU/index причины. Stage/attempt/elapsed и5s read-only index/status snapshots логируются перед teardown при TimeoutException или WebException Timeout/RequestCanceled; исходный exception rethrown, другие HTTP errors не повторяются. Phase candidate build0 errors/96 warnings, server1/1 (37.558s), initial response730.9ms; full ещёне выполнен.

Card fixture source review выявил возможный late setup write: CreateArranged helper задаёт Root2.PlannedBeginDateTime послеConnect, первоначальныйJSON null; hooks используют10ms throttle. SealPendingSaves закрывает producer и tracking editable fields, но не ожидает async-void storage Updating/cache publications. Поэтому поздний setup snapshot может вернуть старыйTitle; конкретное событие CI безtrace ещёне доказано. PlannedBeginDateTime=DateTime.Today перенесён в первоначальныйJSON доConnect, чтобы postConnect assignment былno-op. Перед вводом await2s проверяет актуальные DataContext/Text выбранного parent. Descendant wait2s требует knownnewTitle/Emoji и на parent, и на ancestor с тем жеId, затем exact rendered trail и обаVMfields; live oldoracle больше не принимается. Все focus/input/glyph/tools/ancestry/disk checks прежние. Failure diagnostics печатают cache/ancestor identity, Titles/Emojis, textbox/trail binding state на синтетических данных. Production source не менялся.

### Последний candidate: source и targeted review

Последняя редакция source зафиксирована в `jellyfish/final-source-hashes-last-candidate.json`: собственные production/data файлы совпадают с первоначальным исправленным matcher; отличаются только шесть test files. Build: 0 errors/53 warnings. Exact pair: 4/4, 24.813s; исходная карточечная regression: 1/1, 11.699s. Desktop build: 0 errors/2 Git newline warnings. Все команды завершились exit 0 (`last-candidate-outcomes.json`).

Fresh source/contract/adversarial re-review — PASS, новых HIGH/MEDIUM/LOW findings нет. Reviewer просмотрел все 27 `visual-card-setup-final` PNG: include/exclude, выбранные деревья, ребёнок/внук и четыре перехода согласованы. Кадры сняты до последнего visibility guard; этот guard отдельно проверен последним targeted run. Основной агент дополнительно просмотрел include/exclude selected и grandchild after. Reviewer не менял файлы и не запускал тесты; effective sandbox writable, технически независимая read-only изоляция недоступна. Обязательные full main и Headless последнего source запущены; AC7 остаётся pending, прежние partial/targeted/CI результаты не считаются новым полным PASS.

### Финальный Post-EXEC Review точной пары, 04.10.2026

- Статус: **PASS** по AC1–7. Обязательные свежие unfiltered проекты последнего source завершены; targeted и прежние partial runs в итог не суммируются. Итог этого подраздела заменяет прежний pending exact-pair disposition; история не удалена.
- Scope review: утверждённая SPEC, exact user case, дополнительное поручение «Исправь», matrix/write set, final diff19 files относительно main49bc04e и источник всех шести изменившихся test files. Собственные production/data hashes совпадают с первоначальным corpus-first fix. Других production server/storage/CLI изменений нет; unrelated status-recovery SPEC сохранена вне diff.
- Contract review: AC1/2 — corpus5216 forms, Extract/Remove/Split/public const pattern, legacy compatibility и отрицательные/длинные входы; AC3/4/5 — real Title input, оба popup и rendered tree, child/grandchild с несколькими родителями, четыре перехода до save; AC6 — exact Title/ID/relations read-back. Все соответствующие cases прошли в последнем полном main. Старый выбранный фильтр автоматически не переносится; это прежняя утверждённая семантика.
- Evidence review: `jellyfish/full-main-last-candidate.log` — main1181/1181, 19m31.277s; `full-headless-last-candidate.log` — Headless51/51, 2m26.809s; `full-last-outcomes.json` — оба exit0, source660ee145. Оба проекта без failed/skipped. В main также PASS server54.205s, hydration568ms, original card4.421s и exact card5.835s. Desktop0 errors/2 warnings, exact4/4 и original1/1 подтверждены `last-candidate-outcomes.json`. Все27 PNG просмотрены reviewer, root отдельно проверил ключевые include/exclude/grandchild состояния. Source18-file audit drift0; source/output Unicode notice hash совпал.
- Adversarial review: corpus/legacy boundary, ZWJ/modifiers/VS/flags, новый символ у существующей задачи, общий ключ нескольких эмодзи, несколько предков, независимый выбранный ключ и носитель старого ключа. Известные новые Title/Emoji обязательны на parent и current ancestor, поэтому согласованный старый VM/render state не даёт PASS. До ввода обязательны visible/layout/DataContext/Text; подготовительная дата seeded доConnect, а прежние UI/disk/assertions сохранены. SDK transport первой страницы и отдельный visibility budget не подменены единым ужесточённым10s лимитом; ошибочный вариант и prewarm отклонены. Failure diagnostics ограничены и не меняют production behavior.
- Role-based review: domain workflow — исходные grouping/relations и оба симптома; UX — include/exclude selection и все27 before/after кадры; tester — RED исходного matcher, exact/current full/CI и отсутствие ослабленных assertions; developer — immutable generated trie/legacy fallback, отсутствие новых runtime/schema dependencies; delivery/security — synthetic published fixtures, полный notice, отдельный draft/ready gate, отсутствие личных task files в публикации.
- Fix and re-review: прежние UI readiness/false-old-oracle findings исправлены stronger predicates и seed ordering; cache migrations исключены актуальной metadata всех seed/последующих raw edits; SDK timeout/cancellation и failure snapshots пересмотрены по реальному контракту SDK/Raven. Fresh source/contract/visual reviewer pass — без HIGH/MEDIUM/LOW findings. Его sandbox writable `danger-full-access`: технически независимая read-only изоляция недоступна. Root выполнил отдельный fallback: scope/diff, assertions, frozen hashes, raw suite summaries, CI и выборочные кадры. Точные причины прежних intermittent failures и host scheduling не объявляются доказанными.
- CI source660ee145: [run37211568547](https://github.com/Kibnet/Unlimotion/actions/runs/37211568547), main1181/1181 за6m09.797s, Headless51/51 за1m23.497s, оба0 failed/skipped. CodeQL/Android также SUCCESS. Документальные delivery edits после этого source не меняют проверенный code/test binary; статус следующих CI runs сообщается отдельно.
- Stop decision: локальный EXEC PASS; BLOCKER AC7 закрыт, открытых HIGH/MEDIUM/LOW нет. Ready PR разрешён после финального read-back. Merge, установка и release не поручены.

Fresh expanded reviewer closure подтвердил source/SPEC/evidence PASS и latest HEAD660ee145 CI. Единственный LOW delivery-doc finding: live PR body ещё содержал старый pending full. Исправлен публикацией финального body; `jellyfish/pr-final-validation-readback.json` подтверждает main1181/1181, Headless51/51, CI link и отсутствие прежнего active pending. Source не менялся; техническое ограничение writable reviewer sandbox сохраняется.

Depth checklist: scope/unrelated changes, matrix и AC1–7, реальный control input/selection/render, multiple-parent order, before-save disk и reload, legacy/public-pattern compatibility, corpus provenance/notice, SDK phase budgets/cleanup, latest-source/hash identity, обоих full summaries, CI, private/public boundary и delivery authorization проверены. Targeted, historical и текущие полные результаты различены.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC / диагностика | Найден общий дефект распознавания; индивидуальный пример ещё неизвестен | Read-only regex examples и corpus baseline; код/тесты не менялись | Review SPEC | Дополнительный вопрос задан | Эта SPEC |
| SPEC / review завершён | Уточнены validation и простой корпусный matcher; отозван ложный compatibility counterexample после контрольной проверки | Linter 20/20, rubric 30/30, role/adversarial/fallback review; независимый read-only sandbox недоступен | Получить exact approval | Ожидается «Спеку подтверждаю» | Эта SPEC |
| EXEC / approval | Получена точная фраза пользователя «Спеку подтверждаю» | HEAD прежний; unrelated status-recovery SPEC сохранена | Добавить regression и получить RED | Подтверждено 02.10.2026 | Код/тесты и эта SPEC |
| EXEC / RED | Старый matcher даёт 10/23 failing helper tests, 635/5216 форм нарушают совокупный Extract/Remove/Split/raw-pattern контракт (620 baseline относился только к точному Extract). Include/exclude и trail ребёнка воспроизводят отсутствие 🫶 | `red/helper.log`, `red/ui-filter-diagnostic.log`, card failure в `red/ui-rendered.log`; Skia кадры просмотрены | Corpus-first fix | — | `artifacts/emoji-title-refresh/red/` |
| EXEC / implementation | Trie 17684 regex characters, immutable legacy fallback проверен ordinal относительно HEAD; generator byte-deterministic. No runtime packages/network/schema changes | Helper GREEN23/23, persistence GREEN1/1 | UI re-review и full suites | — | `green/helper.log`, `green/persistence.log` |
| EXEC / review fixes | Seed исходных Title до Connect убирает зависимость подготовки от серии нерелевантных live edits. Добавлены recursive projected child IDs, четыре перехода на обоих popup и after-selection кадры | Первичный `green/ui.log` 2/3: exclude readiness failure; не заявляется product regression или GREEN | Повторить затронутые UI-сценарии | — | `green/ui-reviewed.log` |
| EXEC / UI evidence fixes | Первый full run обнаружил слишком раннее чтение отрисованных элементов. Добавлены layout/ForceRenderTimerTick, ожидание видимого emoji, default font resources и actual TreeViewItem checks. Свежий test binary собран в отдельный candidate output, чтобы не перезаписывать binary текущего full run | `green/final-targeted/ui.log` 3/3; include selected показывает parent+child, exclude selected их убирает; grandchild trail 🛠🫶 просмотрен | Дождаться mandatory suites и baseline двух existing failures | — | `green/final-targeted/*.png` |
| EXEC / full Headless | Свежая runtime-валидация Headless прошла 51/51; первый незавершённый запуск не засчитан, его wrapper остановлен. TUnit/native stdout buffering задерживал появление progress в redirected log | `green/full-headless-final.log`, 51 passed, 0 failed/skipped | Завершить full main и baseline | — | Headless report/log |
| EXEC / final visual re-review | Disk-seed начальных parent Title и before-trail assertions, focus списка перед Escape и closed-popup assertions исправили последние две evidence находки | `green/final-reviewed/ui.log` 3/3; все 27 PNG просмотрены reviewer, main отдельно проверил изменённые before/empty/phoenix состояния | Зафиксировать mandatory suite disposition | — | `green/final-reviewed/` |
| EXEC / provenance | Полный Unicode notice добавлен в production output/publish; generator pointer синхронизирован | Final desktop build 0 errors/5 warnings; source/desktop/candidate notice SHA-256 совпадает; publish не выполнялся | Закончить audit | — | `green/desktop-final-build.log`, notice files |
| EXEC / mandatory main | Полный run завершён 1164/1174, 10 failures; два harness failures исправлены свежим UI run. Exact paired baseline/current проверки отделяют подтверждённые baseline failures от невоспроизведённых/изменившихся симптомов | Failure audit выше; причины всех remaining failures не установлены | Stop NEEDS-FIX по AC7; не объявлять EXEC complete и не расширять production scope незаметно | Нового расширения/доставки не разрешено | `green/full-main.log`, `baseline-*.log`, `current-*.log` |
| EXEC / validation remediation, 03.10 | Пользователь поручил «Исправь». Устранены ранние assertions удаления: await expected cache count/disk absence; Raven fixture ожидает non-stale обеих HTTP query shapes; hydration pump освобождает worker с теми же65 inputs/exact64 barrier/10s bounds и сообщает исходный fault | Первичная hydration diagnostic без поведенческих изменений тоже прошла; thread-pool starvation не считается доказанной причиной. Targeted21/21: hydration1, server1, deletion2, graph1, search7, title1, tree-story1, CLI7; desktop build0 errors | Fresh full main, затем fresh full Headless; AC7 ещё pending | Разрешена локальная доработка validation; Git delivery не поручен | `remediation/build.log`, `hydration-diagnostic.log`, `hydration.log`, `server.log`, `delete-*.log`, `graph-story.log`, `search.log`, `title.log`, `tree-story.log`, `cli.log`, `desktop-build.log` |
| EXEC / review rework | Reviewer и основной агент подтвердили blocking SpinWait внутри headless search-test. Три ожидания initial/filter/restore заменены async polling, predicates и15s budgets сохранены. Title/paste/CLI уже async и targeted зелёные; неподтверждённых правок в них нет | Первый full запуск остановлен основным агентом после проверки PID/executable/arguments именно собственного run; partial log не засчитывается как validation. Production source не менялся | Пересобрать test binary, search7 cases, полный main+Headless с последним write set | — | `remediation/full-main-interrupted-for-review.log`, `build-final.log`, `search-final.log` |
| EXEC / render fix and re-review | Fresh full закончен1172/1174, только два новых toolbar visible-text failures. Ожидание чередует dispatcher/render queues и yield с прежним2s bound, как runtime Avalonia12.0.3. Остальные первоначальные failures прошли в этом полном run | Latest UI3/3 без capture и3/3 с capture, все27новыхPNG просмотрены reviewer; old full относится прежнему binary. Isolated old source без capture тоже2/2, поэтому причинность capture не заявляется абсолютной | Final standard rebuild и full main+Headless | — | `full-main-before-render-fix.log`, `ui-no-capture.log`, `ui-capture.log`, `visual-final/` |
| EXEC / дополнительный class run diagnostic | Filtered whole toolbar class остановился после2passed. Native clrstack не содержит активного UI worker; полный dumpasync также показывает next unchanged `AllToggle_WithReentrantSourceUpdate` ожидающий `HeadlessSessionExtensions.DispatchAsync` и множество `ConstraintKeyScheduler.WaitAndExecuteTestAsync`. Первичный вывод «test-body stacks нет» исправлен после чтения всего async report. Собственный процесс проверен поPID/arguments и остановлен; source runner/package не менялся | Optional class run не засчитывается как полный pass и не заменяет mandatory suite. Конкретная причина pending dispatch не установлена; нельзя приписать её TUnit по одной внешней scheduler stack | Final unfiltered full suite на стандартном output остаётся обязательным | — | `full-toolbar-stalled.log`, `toolbar-stacks.log`, `toolbar-async.log`, dumps; `build-standard-final.log`0 errors |
| EXEC / popup prerequisite diagnostic | Следующий full упал в тех же2cases: transition1, `popup=False`, listBounds0; данные filter уже содержат🫶. Render-only fix не помог. Новая regression использует normal keyboard open/close с явными assertions prerequisites | Этот incomplete full остановлен на verified ownPID; не считается зелёным. Остальные production/modules не менялись | Latest UI on/off, frames re-review, fresh full main+Headless | — | `full-main-popup-closed.log`, `build-popup-final.log`, `ui-popup-no-capture.log` |
| EXEC / latest UI verification | Conditional focus+Escape при открытом popup и focus+Enter после Title edit подтвердили обычный control flow; исходные selection/descendant/disk assertions сохранены | Latest 3/3 без capture, 3/3 с capture; все 27 `visual-popup-final` PNG просмотрены reviewer, основной агент отдельно проверил include/exclude/grandchild | Последний полный main и Headless | — | `remediation/ui-popup-no-capture-final.log`, `ui-popup-capture-final.log`, `visual-popup-final/` |
| EXEC / final mandatory validation | На неизменённом последнем source/test change set завершены последовательные unfiltered main и Headless; старые failures и partial runs не суммируются в pass | Main 1174/1174, 14m37.158s; Headless 51/51, 2m08.701s; оба exit 0, 0 failed/skipped. Desktop 0 errors/5 warnings; все первоначальные failed cases и ordinary mouse/AllToggle coverage passed | Завершить Post-EXEC и обновить active matrix/disposition | — | `remediation/full-main-final.log`, `full-headless-final.log`, `desktop-build.log` |
| EXEC / final review and evidence closure | Активная matrix переведена в PASS; прежний NEEDS-FIX и failure audit явно помечены historical. Source/data hashes и notice повторно сверены, whitespace checks зелёные. Основной adversarial fallback и fresh reviewer re-review завершены PASS; reviewer sandbox writable, ограничение сохранено | AC1–7 закрыты конкретными source/UI/persistence/full-run evidence; открытых BLOCKER/HIGH/MEDIUM/LOW findings нет. Причина всех прежних intermittent failures и optional dispatch stall не заявляется доказанной | Локальный EXEC PASS, итоговый отчёт без Git delivery/установки | Нового разрешения не требуется | Финальный Post-EXEC Review выше, `remediation/final-source-hashes.json` |
| Delivery / PR preflight, 04.10 | Получено поручение «Оформи PR»; GitHub auth/repository/base/ветка и scope проверены. `origin/main` совпадает с проверенной базой, source/data без drift, полный main 1174/1174 и Headless 51/51 остаются применимым evidence | Code/tests review PASS, unrelated SPEC отделена; ignored screenshots/logs будут явно обозначены local-only | Создать ветку, коммит, push и ready PR; проверить публикацию и прикрепить PR к задаче | «Оформи PR» разрешает Git delivery | Эта SPEC и финальные validation artifacts |
| Delivery / PR создан | Коммит `146d5f12` опубликован в `fix/emoji-title-refresh`; PR #315 OPEN/ready, base main, 19 ожидаемых файлов, body прочитан обратно, PR прикреплён к задаче | Исходный validation evidence опубликован в описании с точной пометкой local-only; unrelated SPEC не включена | Review и CI | Git delivery уже разрешён | https://github.com/Kibnet/Unlimotion/pull/315 |
| EXEC / exact user case | Получены пара `🧙‍♂️ → 🪼` и точный commit реального task space. История прочитана без изменений личных задач: Title иначе тот же, legacy key после замены пуст, fixed key `🪼` | Новый пример совпал с исходной причиной. Добавлены 4 exact scenario cases и 2 helper arguments; исходные assertions/fixtures сохраняются | RED старого matcher, fresh targeted/UI frames, оба полных проекта, re-review и обновление PR | Нового behavior approval не требуется: SPEC требовала включить полученный конкретный пример | Synthetic regression; regex evidence в local artifacts |
| EXEC / exact-pair verification | Все4 exact cases и25 helper cases GREEN; все27 PNG просмотрены. В текущем full main повторились Raven-index и first-batch hydration timeouts, при CPU100% и нескольких чужих test processes | AC1–6 подтверждены для точной пары; AC7 NEEDS-FIX/pending, старый CI146d5f12 SUCCESS не считается CI новой coverage | Доставить exact coverage в тот же PR как draft; закончить оба локальных проекта и диагностировать failure с сохранёнными budgets | Ранее «Оформи PR» разрешает Git delivery; установки и изменения личных задач нет | `jellyfish/green-exact.log`, `helper-green.log`, `desktop-build.log`, `visual-green/`, `full-main.log` |
| Delivery / exact-pair coverage | Коммит53d3795a опубликован в том жеPR315, head/body прочитаны обратно; точная пара и draft validation gate присутствуют. Unrelated SPEC не включена | PR draft; новый CI запущен, completion не заявляется | Завершить fixture validation и обновить delivery evidence | Ранее Git delivery разрешён | `jellyfish/pr-readback.json` |
| EXEC / fixture rework after review | Первоначальный own full остановлен для source-backed rework; актуальная metadata seed исключает посторонние migration writes; cold Raven query initialization отделена от postinsert freshness. Production и budgets прежние | Source re-review PASS; targeted hydration1/1 и server1/1, build0 errors. Generic scheduling/I/O cause не объявляется исправленной; обязательный gate ещёpending | Fresh full main и full Headless последнего binary, final re-review | Локальные fixture changes внутри ранее разрешённого scope | `jellyfish/full-main-before-fixture-seed.log`, `build-fixture.log`, `hydration-seed.log`, `server-prewarm.log`, `final-source-hashes-after-fixture.json` |
| EXEC / prewarm hypothesis rejected | В full hydration прошла878ms, Raven postinsert query опять timeout10.076s. Prewarm не помогает и удалён из текущего source; вместо него добавлен bounded failure-only index/state snapshot с исходным throw | Причина Raven timeout неизвестна, AC7 blocker сохраняется. Продолжающийся full/Headless относятся binary предыдущей попытки; новой диагностике не приписываются результаты старого binary | Закончить текущие suites, собрать diagnostic candidate, проверить сервер и новый mandatory набор | Новых product/operational решений не требуется | `jellyfish/full-main-final.log` на момент промежуточной записи, затем отдельная историческая копия |
| EXEC / final phase and UI readiness candidate | Восстановлен исходный SDK budget первого HTTP и отдельный 10s visibility poll; setup-дата карточки задана до Connect; input/trail ждут известные новые значения | Source/contract и все 27 visual frames PASS; build0 errors/53 warnings, exact4/4, original1/1, desktop0 errors/2 warnings. Полные main и Headless выполняются, AC7 pending | Завершить оба mandatory suites на замороженном source, final review и обновить PR | Ранее test remediation и Git delivery разрешены | `jellyfish/*last-candidate*`, `visual-card-setup-final/`, `server-api-phases.log` |
| EXEC / exact-pair mandatory gate закрыт | Оба свежих unfiltered проекта source660ee145 завершены; хеши18 files без drift, notice source/output совпал | Main1181/1181, 19m31.277s; Headless51/51, 2m26.809s; оба exit0, failed/skipped0. CI source также1181/1181 и51/51, CodeQL/Android SUCCESS | Final re-review и ready PR read-back | Нового разрешения не требуется | `jellyfish/full-last-outcomes.json`, `full-*-last-candidate.log`, `ci-660ee145.log`; финальный Post-EXEC выше |
| Delivery / final validation body | Expanded source/SPEC/evidence review PASS; единственный LOW stale PR body исправлен, live read-back содержит свежие full/CI counts | Base main49bc04e не изменилась, PR mergeable; код18 files без drift. Только финальная документальная фиксация SPEC остаётся локально | Опубликовать SPEC commit, перевести PR315 в ready и проверить final Head/isDraft/body | «Оформи PR» ранее разрешает все эти действия; merge/установка не поручены | `jellyfish/pr-final-validation-readback.json`, final source hashes и review выше |
