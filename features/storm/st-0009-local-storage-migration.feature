# language: ru
@feature:GF-009 @story:ST-0009 @need:ND-0004
Функция: Сохранять локальные задачи и безопасно мигрировать legacy-данные
  Как пользователь, я храню задачи в локальных JSON-файлах, а приложение загружает, чинит и мигрирует старые данные без ручного вмешательства.
  Ценность: Local-first обещание невозможно без надёжного storage contract.

  @rule:GR-025 @feature:GF-009 @scenario:SC-0009-001 @story:ST-0009 @need:ND-0004 @constraint:CN-0001 @constraint:CN-0002 @coverage:regression @automated @test:TS-0014
  Правило: Задачи сериализуются в локальные JSON-файлы в выбранной папке.
    Сценарий: Задачи сериализуются в локальные JSON-файлы в выбранной папке.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0009
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда Задачи сериализуются в локальные JSON-файлы в выбранной папке.

  @rule:GR-026 @feature:GF-009 @scenario:SC-0009-002 @story:ST-0009 @need:ND-0004 @constraint:CN-0001 @constraint:CN-0003 @constraint:CN-0002 @coverage:regression @automated @test:TS-0003 @test:TS-0014
  Правило: Миграции восстанавливают reverse links, status model и availability при загрузке.
    Сценарий: Миграции восстанавливают reverse links, status model и availability при загрузке.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0009
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда Миграции восстанавливают reverse links, status model и availability при загрузке.

  @rule:GR-027 @feature:GF-009 @scenario:SC-0009-003 @story:ST-0009 @need:ND-0004 @constraint:CN-0001 @constraint:CN-0002 @coverage:regression @automated @test:TS-0014
  Правило: Восстановление JSON и исключение migration reports защищают загрузку от некорректных файлов.
    Сценарий: Восстановление JSON и исключение migration reports защищают загрузку от некорректных файлов.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0009
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда Восстановление JSON и исключение migration reports защищают загрузку от некорректных файлов.
