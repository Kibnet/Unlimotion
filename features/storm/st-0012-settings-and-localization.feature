# language: ru
@feature:GF-012 @story:ST-0012 @need:ND-0005
Функция: Настраивать appearance, storage, backup, updates и localization из Settings
  Как пользователь, я управляю языком, темой, шрифтом, fuzzy search, storage mode, Git backup и обновлениями в одном Settings flow.
  Ценность: Settings делают сложные возможности управляемыми без ручной правки файлов.

  @rule:GR-034 @feature:GF-012 @scenario:SC-0012-001 @story:ST-0012 @need:ND-0005 @constraint:CN-0004 @constraint:CN-0005 @constraint:CN-0006 @coverage:happy_path @automated @test:TS-0008 @test:TS-0012
  Правило: Настройки поддерживают параметры внешнего вида: язык, тему, масштаб шрифта и fuzzy search.
    Сценарий: Настройки поддерживают параметры внешнего вида: язык, тему, масштаб шрифта и fuzzy search.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0012
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда Настройки поддерживают параметры внешнего вида: язык, тему, масштаб шрифта и fuzzy search.

  @rule:GR-035 @feature:GF-012 @scenario:SC-0012-002 @story:ST-0012 @need:ND-0005 @constraint:CN-0004 @constraint:CN-0005 @coverage:constraint_check @automated @test:TS-0008 @test:TS-0009
  Правило: Настройки поддерживают локальное/серверное хранилище, Git backup и действия разрешения конфликтов.
    Сценарий: Настройки поддерживают локальное/серверное хранилище, Git backup и действия разрешения конфликт…
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0012
      Когда пользователь запускает или проверяет remote backup flow
      Тогда Настройки поддерживают локальное/серверное хранилище, Git backup и действия разрешения конфликтов.

  @rule:GR-036 @feature:GF-012 @scenario:SC-0012-003 @story:ST-0012 @need:ND-0005 @constraint:CN-0004 @constraint:CN-0005 @constraint:CN-0007 @constraint:CN-0008 @coverage:constraint_check @automated @test:TS-0008 @test:TS-0015
  Правило: Контролы обновления и compatibility checks защищают release/update flow.
    Сценарий: Контролы обновления и compatibility checks защищают release/update flow.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0012
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда Контролы обновления и compatibility checks защищают release/update flow.
