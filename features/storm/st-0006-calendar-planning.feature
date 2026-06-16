# language: ru
@feature:GF-006 @story:ST-0006 @need:ND-0002
Функция: Планировать задачи через даты, длительность, повторения, wanted и importance
  Как пользователь, я задаю плановые даты, длительность, повторение, желание и важность, чтобы граф задач отражал приоритет и ритм работы.
  Ценность: Планировочные поля помогают превратить структуру задач в практический schedule.

  @rule:GR-016 @feature:GF-006 @scenario:SC-0006-001 @story:ST-0006 @need:ND-0002 @constraint:CN-0003 @constraint:CN-0004 @coverage:happy_path @automated @test:TS-0005 @test:TS-0013
  Правило: Задачи поддерживают planned begin/end/duration и быстрые контролы дедлайна.
    Сценарий: Задачи поддерживают planned begin/end/duration и быстрые контролы дедлайна.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0006
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда Задачи поддерживают planned begin/end/duration и быстрые контролы дедлайна.

  @rule:GR-017 @feature:GF-006 @scenario:SC-0006-002 @story:ST-0006 @need:ND-0002 @coverage:happy_path @automated @test:TS-0013
  Правило: RepeaterPattern поддерживает none/daily/weekly/monthly/yearly и after-complete режим.
    Сценарий: RepeaterPattern поддерживает none/daily/weekly/monthly/yearly и after-complete режим.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0006
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда RepeaterPattern поддерживает none/daily/weekly/monthly/yearly и after-complete режим.

  @rule:GR-018 @feature:GF-006 @scenario:SC-0006-003 @story:ST-0006 @need:ND-0002 @constraint:CN-0003 @constraint:CN-0004 @coverage:constraint_check @automated @test:TS-0005 @test:TS-0013
  Правило: Wanted и importance доступны в UI и участвуют в представлении и фильтрации задач.
    Сценарий: Wanted и importance доступны в UI и участвуют в представлении и фильтрации задач.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0006
      Когда пользователь ищет или фильтрует задачи в текущем представлении
      Тогда Wanted и importance доступны в UI и участвуют в представлении и фильтрации задач.
