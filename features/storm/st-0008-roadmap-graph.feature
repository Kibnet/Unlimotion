# language: ru
@feature:GF-008 @story:ST-0008 @need:ND-0003
Функция: Визуализировать и менять граф задач через Roadmap
  Как пользователь, я вижу связи задач на roadmap-графе, фильтрую представление и взаимодействую с узлами без потери task context.
  Ценность: Граф делает зависимости и структуру работы обзорными.

  @rule:GR-022 @feature:GF-008 @scenario:SC-0008-001 @story:ST-0008 @need:ND-0003 @constraint:CN-0004 @coverage:happy_path @automated @test:TS-0007
  Правило: Roadmap строит узлы и связи из текущей модели задач.
    Сценарий: Roadmap строит узлы и связи из текущей модели задач.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0008
      Когда пользователь работает с дорожной картой задач
      Тогда Roadmap строит узлы и связи из текущей модели задач.

  @rule:GR-023 @feature:GF-008 @scenario:SC-0008-002 @story:ST-0008 @need:ND-0003 @constraint:CN-0004 @coverage:happy_path @automated @test:TS-0007
  Правило: Компоновка остаётся читаемой и покрыта регрессионными тестами для viewport и overlay-состояний.
    Сценарий: Компоновка остаётся читаемой и покрыта регрессионными тестами для viewport и overlay-состояний.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0008
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда Компоновка остаётся читаемой и покрыта регрессионными тестами для viewport и overlay-состояний.

  @rule:GR-024 @feature:GF-008 @scenario:SC-0008-003 @story:ST-0008 @need:ND-0003 @constraint:CN-0004 @coverage:happy_path @automated @test:TS-0006 @test:TS-0007
  Правило: Roadmap поддерживает фильтры, inline rename, multi-selection и overlay/minimap controls согласно спекам.
    Сценарий: Roadmap поддерживает фильтры, inline rename, multi-selection и overlay/minimap controls согласн…
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0008
      Когда пользователь ищет или фильтрует задачи в текущем представлении
      Тогда Roadmap поддерживает фильтры, inline rename, multi-selection и overlay/minimap controls согласно спекам.
