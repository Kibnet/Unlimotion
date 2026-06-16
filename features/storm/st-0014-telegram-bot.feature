# language: ru
@feature:GF-014 @story:ST-0014 @need:ND-0006
Функция: Получать доступ к задачам через Telegram bot
  Как пользователь внешнего канала, я могу искать и открывать задачи, создавать связанные задачи и менять статусы через Telegram commands/callbacks, если bot surface считается поддерживаемой.
  Ценность: Telegram может дать быстрый mobile/external access без полного клиента.

  @rule:GR-039 @feature:GF-014 @scenario:SC-0014-001 @story:ST-0014 @need:ND-0006 @coverage:happy_path @draft
  Правило: Бот ограничивает доступ allowed users и поддерживает /start, /help, /search, /task и /root.
    Сценарий: Бот ограничивает доступ allowed users и поддерживает /start, /help, /search, /task и /root.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0014
      Когда пользователь обращается к задачам через Telegram bot
      Тогда Бот ограничивает доступ allowed users и поддерживает /start, /help, /search, /task и /root.

  @rule:GR-040 @feature:GF-014 @scenario:SC-0014-002 @story:ST-0014 @need:ND-0006 @coverage:business_rule @draft
  Правило: Callback-действия позволяют открыть задачу, создать sub/sibling, изменить статус, удалить, смотреть отношения и использовать file storage/Git timers при настройке.
    Сценарий: Callback-действия позволяют открыть задачу, создать sub/sibling, изменить статус, удалить, смот…
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0014
      Когда пользователь создаёт или добавляет задачу через доступное действие интерфейса
      Тогда Callback-действия позволяют открыть задачу, создать sub/sibling, изменить статус, удалить, смотреть отношения и использовать file storage/Git timers при настройке.
