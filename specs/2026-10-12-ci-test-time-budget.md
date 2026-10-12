# Дать полному CI время завершить оба набора

## Контекст

Short SPEC: одна граница времени в .github/workflows/tests.yml, без product/API/test assertion изменений. Дополнение validation gate Android-задачи e74c5d1a, PR325. User разрешил последовательное выполнение и automatic SPEC approval после review; лишних решений пользователя нет. Central review-loops/testing-baseline/github delivery, локальный UI MUST учитываются: UI behavior не меняется, оба полных набора сохраняются.

CI38257636361 дляfc1f5ac9: Main step success, Headless step cancelled. Check-run114839715954 annotation: "The job has exceeded the maximum execution time of 30m0s". Concurrency scoped по PR/ref, evidence не указывает на superseding run. Итог cancelled не PASS; прежние failed/cancelled записи сохраняются.

## Пять содержательных проверок

| Проверка | Решение / ожидаемый результат | План → evidence |
| --- | --- | --- |
| 1. Результат / границы | all-tests timeout-minutes30→45; последовательные restore/build/Main/Headless/report/upload остаются. Не менять отдельные тестовые дедлайны, минимальное число tests, assertions, cancellation/concurrency и не skip suites | Один yaml scalar; полная CI проверка на новом exact head |
| 2. Решения / разрешения | 45 минут — конечный бюджет, достаточный для наблюдаемого почти30-minute Main+build и ещё примерно4-minute Headless с запасом. Увеличение на15 минут не является ускорением приложения | API annotation и step times, local fullHeadless4m02. Review PASS→automatic approval; не спрашивать пользователя о routine internal bound |
| 3. AC→evidence | AC1 толькоscalar30→45, YAML syntax/structure валидны. AC2 исходный cancelled run сохранён и не переименован в PASS. AC3 повтор полного CI на exact head, оба набора и итог job success с TRX counts; Root Android5/1/5 остаётся отдельным gate | Static diff + pinned existing workflow/script contracts, actual GH run/artifacts. При отмене/сбое выяснить причину; один повтор не обещает успех |
| 4. Существенный риск / rollback | Зависший job может ждать до45 минут; tests/stage safeguards и конечныйjobtimeout остаются. No credential/deploy/product change. Rollback revert отдельногоci commit | Сравнить diff/conditions и newrun outcomes; не увеличивать deadline повторно без нового evidence |
| 5. Findings / disposition / stop | Root сверил annotation, steps, scoped concurrency и30-minutebound. Independent reviewer подтвердил конечный бюджет и сохранение обоих suites, findings отсутствуют | PostSPEC PASS; postEXEC pending до exacthead fullCI. Вся Android задача не завершена |

## Quality gate / approval

Post-SPEC: PASS, независимый review migration_spec_review; findings отсутствуют. Root fallback: исходная ошибка именноjobbudget, Mainsuccess не приравнивается к wholePASS, условия обоих suites остаются. Reviewer проверил Main TRX: 1537/1537, failed/skipped0; stage26m12s, headShafc1f5ac9, checkout PR merge ref26b2f081. Это не wholejob PASS. Reviewer работал только чтением, но техническая read-only sandbox isolation отсутствует.

Post-EXEC: не выполнен.

SPEC автоматически подтверждена после PASS по прямому поручению пользователя. Это не разрешение merge/complete основной задачи безAndroid acceptance.
