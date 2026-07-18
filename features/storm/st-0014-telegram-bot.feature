# language: ru
@feature:GF-014 @story:ST-0014 @need:ND-0006
Функция: Получать доступ к задачам через Telegram bot
  Как пользователь внешнего канала, я могу искать и открывать задачи, создавать связанные задачи и менять статусы через Telegram commands/callbacks, если bot surface считается поддерживаемой.
  Ценность: Telegram может дать быстрый mobile/external access без полного клиента.

  @rule:GR-039 @feature:GF-014 @scenario:SC-0014-001 @story:ST-0014 @need:ND-0006 @coverage:happy_path @passing @test:TS-0022
  Правило: Бот ограничивает доступ allowed users и поддерживает /start, /help, /search, /task и /root.
    Сценарий: Бот ограничивает доступ allowed users и поддерживает /start, /help, /search, /task и /root.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0014
      Когда пользователь обращается к задачам через Telegram bot
      Тогда Бот ограничивает доступ allowed users и поддерживает /start, /help, /search, /task и /root.

  @rule:GR-040 @feature:GF-014 @scenario:SC-0014-002 @story:ST-0014 @need:ND-0006 @coverage:business_rule @passing @test:TS-0025
  Правило: Git timers не запускают синхронизацию, пока идет разрешение конфликтов.
    Сценарий: Git timers пропускают pull и push во время разрешения конфликтов.
      Дано у пользователя включены Telegram Git timers
      И в Git backup идет разрешение конфликтов
      Когда срабатывают pull и push timer события Telegram bot
      Тогда бот не выполняет pull и commit/push до завершения разрешения конфликтов.

  @rule:GR-040 @feature:GF-014 @scenario:SC-0014-003 @story:ST-0014 @need:ND-0006 @coverage:business_rule @passing @test:TS-0023
  Правило: Callback-действия открывают задачу, меняют статус, удаляют и показывают отношения.
    Сценарий: Callback-действия открывают задачу, меняют статус, удаляют и показывают отношения.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И пользователь входит в allowed users Telegram bot
      Когда пользователь выполняет callback-действие Telegram bot для выбранной задачи
      Тогда бот открывает задачу, меняет статус, удаляет задачу, создаёт prompt для sub/sibling и показывает relation lists без раскрытия данных неразрешённым пользователям.
