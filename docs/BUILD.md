# Сборка и работа с ресурсами

Готовая DLL поставляется в архиве на [странице релиза](https://github.com/zzest-dev/LegacyMapExpansion/releases/tag/v1.0.0). Эта инструкция предназначена для сборки исходников и изменения ресурсов.

## Сборка DLL

1. Клонируйте репозиторий.
2. Для сборки релиза выполните `git checkout v1.0.0`. Установите Git LFS, выполните `git lfs install`, затем `git lfs pull`.
3. Установите Python 3 и .NET SDK. Целевая платформа — .NET Framework 4.7.2. При отсутствии reference assemblies установите Developer Pack 4.7.2.
4. Укажите каталог PC 9.4 с установленным BepInEx 5:

```powershell
.\tools\build.ps1 -GameDir 'D:\Games\MadOut2_BigCityOnline'
```

Скрипт проверяет хеши встроенных ресурсов, затем запускает MSBuild. Проект читает игровые зависимости из `game_Data/Managed`, а BepInEx и Harmony — из `BepInEx/core`. Игровые DLL не копируются в результат. Publicizer 0.4.3 предоставляет доступ к внутренним типам при компиляции и не перезаписывает установленную игру.

Прямой эквивалент сборки:

```powershell
dotnet build LegacyMapExpansion.csproj -c Release '-p:GameDir=D:\Games\MadOut2_BigCityOnline'
```

Проверку ресурсов всё равно выполните через `python tools/verify_resources.py --runtime-only`. Без `GameDir` проект выдаёт явную ошибку.

## Восстановление подготовленного Unity-проекта

```powershell
python tools/prepare_unity.py
```

Скрипт проверяет SHA-256 файлов, распаковывает только ассеты в `UnityProject/Assets` и синхронизирует `Assets/regions.json` с `porting/regions.json`. Его повторный запуск восстанавливает исходные подготовленные ассеты поверх локальных изменений: сначала сохраните свои правки.

В архиве находятся готовые префабы, текстуры, материалы, исходные упакованные меши, восстановленные Mesh-ассеты, региональный рельеф и входные JSON. Их `.meta` сохранены для стабильных GUID. Редакторские скрипты и шейдеры хранятся отдельно обычным текстом. Library не поставляется: Unity создаст кеш при первом открытии.

Версия редактора: **2019.1.8f1**, платформа сборки бандла: **StandaloneWindows64**. Первая загрузка требует импорта ассетов. Для сборки шейдеров не используйте `-nographics`.

### Сборка для Linux

Установите модуль **Linux Build Support** для Unity 2019.1.8f1. Для Linux x86_64 используйте `StandaloneLinux64`. Запустите редактор с `-buildTarget Linux64 -mapLinux` и методом `MapPortBuild.Repack`. При сборке на Windows добавьте `-force-glcore`: встроенная проверка шейдеров Linux должна выполняться через OpenGL, а не Direct3D. Бандл будет создан отдельно в `Resources/Linux/map1018.bundle`; Windows-бандл не перезаписывается.

```powershell
& 'D:\Unity\2019.1.8f1\Editor\Unity.exe' -batchmode -quit -force-glcore -buildTarget Linux64 -mapLinux -projectPath "$PWD\UnityProject" -executeMethod MapPortBuild.Repack -logFile "$PWD\unity-linux-build.log"
```

После проверки бандла добавьте или обновите его размер и SHA-256 в `resources.lock.json`, затем выполните:

```powershell
python tools/verify_resources.py --runtime-only --platform Linux
.\tools\build.ps1 -GameDir 'D:\Games\MadOut2_BigCityOnline' -Platform Linux
python tools/package.py --platform Linux
```

DLL находится в `bin/Release/Linux/LegacyMapExpansion.dll`, архив в `dist/LegacyMapExpansion-1.0.0-Linux.zip`. Версия карты остаётся 1.0.0. Платформу задаёт встроенный AssetBundle; переименование Windows-DLL не создаёт Linux-сборку.

Управляемый код Mono не привязан к Windows, однако игровые DLL должны соответствовать серверу. Для проверки по библиотекам конкретного сервера передайте его каталог в `-GameDir`, а при другом имени каталога данных также `-ManagedDir 'D:\Server\server_Data\Managed'`. Запуск и совместимость с конкретной серверной сборкой проверяются отдельно. Инструкция установки: [INSTALL_LINUX.md](INSTALL_LINUX.md).

## Методы сборки Unity

Запускайте методы в закрытом для других экземпляров Unity проекте. Пример PowerShell:

```powershell
$editor = 'D:\Unity\2019.1.8f1\Editor\Unity.exe'
$project = Join-Path $PWD 'UnityProject'
$build = Start-Process -FilePath $editor -WindowStyle Hidden -Wait -PassThru -ArgumentList @('-batchmode', '-quit', '-projectPath', "`"$project`"", '-executeMethod', 'MapPortBuild.Repack', '-logFile', "`"$(Join-Path $PWD 'unity-build.log')`"")
if ($build.ExitCode -ne 0) { throw 'Unity build failed' }
```

| Метод | Когда применять |
| --- | --- |
| `MapPortBuild.Repack` | Пересобрать готовый набор ассетов и применить настройку материалов по `Assets/map-manifest.json` |
| `MapPortBuild.Build` | Восстановить меши/назначения и собрать набор из `Assets/build-input.json` |
| `RegionalMapBuild.Build` | Пересоставить региональный рельеф из исходных мешей и областей, затем собрать и проверить бандл |
| `MapPortBuild.ValidateBundle` | Проверить готовый бандл, рендереры, коллайдеры и raycast по рельефу |
| `MapPortBuild.CompareGround` | Получить сравнение изменённых и исходных тайлов |

Выходной бандл — `Resources/map1018.bundle`. Методы сборки также создают отчёты рядом с проектом; они исключены из Git. После изменения рельефа проверьте границы областей и попадание коллайдера в каждый submesh. Не меняйте только `porting/regions.json`, ожидая автоматического изменения расстановки в уже готовом бинарнике: выборка размещений MapPro — отдельный этап.

## Контроль ресурсов после намеренного изменения

`resources.lock.json` фиксирует поставленную версию. После пересборки проверка хешей закономерно перестанет проходить. Проверьте результат, затем обновите размер и SHA-256 **только изменённых файлов**, например:

```powershell
(Get-Item Resources/map1018.bundle).Length
(Get-FileHash Resources/map1018.bundle -Algorithm SHA256).Hash.ToLowerInvariant()
```

Если изменены подготовленные Unity-ассеты, обновите `PortingInputs/UnityAssets.zip`, сохранив структуру `Assets/...` и `.meta`; не добавляйте в него Editor, MapShaders, TerrainLayers.shader, Library, логи и тестовые сцены. Обновите соответствующую запись lock-файла. Архив хранит исходные ассеты, поэтому изменение одного бандла без обновления ассетов делает дальнейшую пересборку неполной.

Затем выполните проверку ресурсов, сборку DLL и упаковку:

```powershell
python tools/verify_resources.py
.\tools\build.ps1 -GameDir 'D:\Games\MadOut2_BigCityOnline'
python tools/package.py
```

`package.py` включает только DLL и инструкцию по установке. Он не устанавливает плагин в игру и не публикует архив автоматически.
