# Legacy Map Expansion

Отдельный плагин BepInEx для MadOut2 PC 9.4. Переносит статическую геометрию карты 10.18 и выбранные локации 10.24: автошколу возле автодрома, офис и донерчик возле парка с колесом обозрения. Новая кафешка 10.24 не включена.

Версия исходников: **0.2.1**. Плагин собирается в одну DLL со встроенными картой, AssetBundle и таблицами совместимости. Оригинальный `StreamingAssets/Map.bytes` на диске не заменяется.

## Установка

Нужны PC 9.4 и установленный BepInEx 5. Поместите `LegacyMapExpansion.dll` в `BepInEx/plugins/LegacyMapExpansion/` и перезапустите игру. Удалите предыдущую копию этого же плагина из других папок `plugins`.

Подробности, отключение и диагностика: [INSTALL.md](docs/INSTALL.md).

## Сборка исходников

Нужны Git LFS, Python 3, .NET SDK с поддержкой сборки net472 и локальные DLL игры. Unity требуется только для изменения и пересборки ресурсов.

```powershell
git clone https://github.com/zzest-dev/LegacyMapExpansion.git
cd LegacyMapExpansion
git lfs pull
python tools/verify_resources.py
.\tools\build.ps1 -GameDir 'D:\Games\MadOut2_BigCityOnline'
python tools/package.py
```

Готовая DLL: `bin/Release/LegacyMapExpansion.dll`. Установочный архив: `dist/LegacyMapExpansion-0.2.1.zip`.

Для редактирования подготовленных Unity-ресурсов:

```powershell
python tools/prepare_unity.py
```

Откройте `UnityProject` в **Unity 2019.1.8f1**. Инструкция по пересборке: [BUILD.md](docs/BUILD.md).

## Что находится в репозитории

| Путь | Назначение |
| --- | --- |
| `src/` | Загрузка ресурсов, Harmony-патчи, восстановление поведения объектов |
| `Resources/` | Готовые ресурсы, встраиваемые в DLL |
| `UnityProject/` | Редакторские сборщики и собственные шейдеры |
| `PortingInputs/UnityAssets.zip` | Подготовленные Unity-ассеты с сохранёнными `.meta` |
| `porting/` | Области переноса, варианты GUID и точные ссылки на исходные меши |
| `tools/` | Проверка ресурсов, восстановление Unity-проекта, чтение MapPro, сборка и упаковка |
| `resources.lock.json` | Размеры и SHA-256 поставляемых ресурсов |

Бандл, бинарник карты и архив Unity-ассетов хранятся в Git LFS. Кнопка Download ZIP на GitHub не заменяет `git lfs pull`, если в архив попали указатели LFS.

## Документация

- [Как работает плагин](docs/ARCHITECTURE.md)
- [Перенос объектов и новых локаций](docs/OBJECT_PORTING.md)
- [Сборка и редактирование ресурсов](docs/BUILD.md)
- [Используемая структура MapPro](docs/MAP_FORMAT.md)
- [Проверки и ограничения](docs/TESTING.md)
- [Изменения версии 0.2.1](CHANGELOG.md)

Совместимость проверяется по SHA-256 исходной карты из `Resources/compatibility.json`. Если карта другая, плагин отключает свои патчи и оставляет штатную загрузку. Игровые библиотеки, декомпилированные проекты, кеш Unity и временные исследовательские файлы в репозиторий не включены.
