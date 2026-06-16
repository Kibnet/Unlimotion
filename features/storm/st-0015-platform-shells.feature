# language: ru
@feature:GF-015 @story:ST-0015 @need:ND-0005 @need:ND-0006
Функция: Собирать, обновлять и проверять cross-platform application shells
  Как maintainer, я могу собирать desktop shell, проверять startup/update flows и иметь platform projects для Android, browser и iOS вокруг общей Avalonia task UI.
  Ценность: Платформенные shell-проекты расширяют доступность продукта, но поддержка зрелее всего выглядит для desktop.

  @rule:GR-041 @feature:GF-015 @scenario:SC-0015-001 @story:ST-0015 @need:ND-0005 @need:ND-0006 @constraint:CN-0004 @constraint:CN-0007 @constraint:CN-0008 @coverage:compatibility @automated @test:TS-0011 @test:TS-0015
  Правило: Десктопная оболочка собирается как Avalonia WinExe и связана с update/package проверками.
    Сценарий: Десктопная оболочка собирается как Avalonia WinExe и связана с update/package проверками.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0015
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда Десктопная оболочка собирается как Avalonia WinExe и связана с update/package проверками.

  @rule:GR-042 @feature:GF-015 @scenario:SC-0015-002 @story:ST-0015 @need:ND-0005 @need:ND-0006 @constraint:CN-0004 @constraint:CN-0007 @constraint:CN-0008 @coverage:constraint_check @automated @test:TS-0015
  Правило: Android, browser и iOS projects существуют и подключают общую UI-модель, но зрелость требует продуктового подтверждения.
    Сценарий: Android, browser и iOS projects существуют и подключают общую UI-модель, но зрелость требует пр…
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0015
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда Android, browser и iOS projects существуют и подключают общую UI-модель, но зрелость требует продуктового подтверждения.

  @rule:GR-043 @feature:GF-015 @scenario:SC-0015-003 @story:ST-0015 @need:ND-0005 @need:ND-0006 @constraint:CN-0004 @constraint:CN-0007 @constraint:CN-0008 @coverage:constraint_check @automated @test:TS-0011 @test:TS-0015
  Правило: CI и README media automation дают smoke/regression-доказательства для UI-потоков.
    Сценарий: CI и README media automation дают smoke/regression-доказательства для UI-потоков.
      Дано у пользователя открыт актуальный набор задач Unlimotion
      И поведение относится к истории ST-0015
      Когда пользователь выполняет действие, описанное в критерии приёмки
      Тогда CI и README media automation дают smoke/regression-доказательства для UI-потоков.
