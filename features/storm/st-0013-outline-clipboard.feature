# language: ru
@feature:GF-013 @story:ST-0013 @need:ND-0001
Функция: Копировать и вставлять task outlines через clipboard
  Как пользователь, я копирую задачи как markdown outline и вставляю outline обратно в приложение с preview, чтобы переносить структуру задач между текстом и Unlimotion.
  Ценность: Outline clipboard соединяет task graph с внешними markdown workflows.

  @rule:GR-037 @feature:GF-013 @scenario:SC-0013-001 @story:ST-0013 @need:ND-0001 @constraint:CN-0002 @constraint:CN-0004 @constraint:CN-0007 @coverage:happy_path @automated @test:TS-0001 @test:TS-0004 @test:TS-0010
  Правило: Копирование может вывести markdown outline и description по выбранной задаче или поддереву.
    Сценарий: Копирование может вывести markdown outline и description по выбранной задаче или поддереву.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0013
      Когда пользователь копирует или вставляет outline задач
      Тогда Копирование может вывести markdown outline и description по выбранной задаче или поддереву.

  @rule:GR-038 @feature:GF-013 @scenario:SC-0013-002 @story:ST-0013 @need:ND-0001 @constraint:CN-0002 @constraint:CN-0004 @constraint:CN-0007 @coverage:happy_path @automated @test:TS-0001 @test:TS-0004 @test:TS-0010
  Правило: Предпросмотр вставки показывает будущие задачи и создаёт дерево после подтверждения.
    Сценарий: Предпросмотр вставки показывает будущие задачи и создаёт дерево после подтверждения.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0013
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда Предпросмотр вставки показывает будущие задачи и создаёт дерево после подтверждения.
