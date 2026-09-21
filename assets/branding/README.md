# Иконка Unlimotion E

Утверждённый вариант E — круглые петли и чёткая белая подложка с выносом 20 единиц.
Световая композиция унаследована от D — кадр 315/720 (10.5 секунды).
`tools/Unlimotion.IconGenerator/FrozenIconD.cs` — независимый зафиксированный векторный источник;
он не ссылается на текущую анимацию. Большой master экспортируется напрямую из вектора.
Для 16–48 px оставлены более крупные росчерки и усилена кромка.
Для иконок применяется выбранная композиция B: исходный E без поворота,
холст 444 единицы вместо 480 (силуэт 426, поля по 9); масштаб больше на 8,1%.
`readme-logo-512.png` сохраняет исходный E на холсте 480 для обоих README.
Дорожки почти чёрные, блики фиолетово-синие; фоновых пятен и размытого ореола нет.
Белая подложка замкнута, включая разрывы u/n. Центры петель (150,125)/(330,125),
радиус дорожек 80, наружный радиус подложки 123, круглые отверстия радиуса 37.
Живая анимация не меняется. Исторические имена `FrozenIconD` и `master-D-*`
сохранены для совместимости путей, но содержат утверждённый E.

Из корня репозитория (.NET 10):

```powershell
dotnet run --project tools/Unlimotion.IconGenerator -- .
dotnet run --project tools/Unlimotion.IconGenerator -- . --check
dotnet run --project tools/Unlimotion.IconGenerator -- . --check --self-test --preview
pwsh -File scripts/test-icon-resources.ps1
```

Генератор сначала собирает и декодирует весь комплект в памяти, затем записывает файлы.
`--check` ничего не меняет: повторно рендерит и сравнивает каждый файл, включая manifest.
Отсутствующий, повреждённый или устаревший ресурс приводит к ненулевому exit code.
Хеши зависят от зафиксированного графического runtime; воспроизводимость проверена в одной среде,
межплатформенная побитовая идентичность Skia не предполагается.
`--self-test` проверяет отрицательные fixtures missing/corrupt; `--preview` сохраняет контактный лист
в `artifacts/validation/icon-d/contact-sheet.png` (локальный evidence, не publication asset).

- `master-D-2048.png`: прозрачный master; `master-D-opaque-2048.png`: светлый непрозрачный.
- `png/`: 16–2048 px, прозрачные варианты.
- ICO: 16/24/32/48/64/128/256, shared window + Desktop + оба Browser entry points.
- ICNS: 16–1024, включая Retina slots.
- Linux: PNG hicolor 16/24/32/48/64/128/256/512; desktop icon name `unlimotion`.
- Android: legacy mipmaps mdpi…xxxhdpi; adaptive foreground 108dp с масштабом 69% и белым background.
- F-Droid: обе локали получают непрозрачные PNG 512.
- iOS: полная матрица iPhone/iPad + непрозрачный RGB store icon 1024.
- Browser: favicon, 32/192/512 PNG, apple-touch 180.

Никакие публикации генератор не выполняет. Проверка файлов не заменяет нативную сборку и осмотр
launcher/dock соответствующей ОС. Иконка ОС может кешироваться; глобальный кэш автоматически не чистим.

Форматы: [Apple AppIcon](https://developer.apple.com/library/archive/documentation/Xcode/Reference/xcode_ref-Asset_Catalog_Format/AppIconType.html),
[Android adaptive icons](https://developer.android.com/develop/ui/compose/system/icon_design_adaptive).
