# Zapret Manager

Красивый Windows-интерфейс для запуска стратегий zapret без bat-окон и консоли.

## Что это

Zapret Manager заменяет ручной запуск `general*.bat` на нормальное приложение с кнопками:

- выбор стратегии zapret;
- запуск и остановка обхода;
- установка выбранной стратегии как службы Windows;
- удаление служб zapret / WinDivert;
- Game Filter: `выкл`, `TCP+UDP`, `TCP`, `UDP`;
- IPSet: `none`, `loaded`, `any`;
- обновление IPSet и hosts;
- диагностика;
- быстрый тест Discord;
- очистка кэша Discord;
- удаление конфликтующих служб.

Программа собрана в один `exe`. Внутри уже лежат нужные файлы zapret, поэтому пользователю не нужно запускать bat-файлы руками.

## Скачать

Готовая программа лежит в разделе **Releases**:

👉 [Скачать Zapret Manager.exe](https://github.com/Ic3V0ke/Zapret-web/releases/latest)

## Как пользоваться

1. Скачай `Zapret Manager.exe`.
2. Запусти файл.
3. Нажми `Да`, если Windows попросит права администратора.
4. Выбери стратегию слева.
5. Нажми `Запустить`.

## Важно

Программа требует права администратора, потому что zapret использует WinDivert для работы с сетевым трафиком.

Антивирус может ругаться на WinDivert. Это типично для подобных сетевых инструментов.

## Исходный код

Исходники интерфейса находятся в папке:

```text
ZapretManager/
```

Основные файлы:

```text
ZapretManager/Program.cs
ZapretManager/ZapretManager.csproj
ZapretManager/app.manifest
```

## Сборка

```powershell
dotnet publish .\ZapretManager\ZapretManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\publish
```

Готовый файл появится здесь:

```text
publish/Zapret Manager.exe
```

## Основа

Внутри используется zapret-discord-youtube / zapret / WinDivert. Этот проект делает удобный интерфейс поверх готовых стратегий.
