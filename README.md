# XbimStudio

Приложение для просмотра и анализа IFC-моделей (BIM) на базе [XBIM Toolkit](https://github.com/xBimTeam). Решение построено на .NET Framework 4.7.2 (WPF/WinForms).

## Структура решения

Файл решения: `Xbim.WindowsUI.sln`

| Проект | Назначение |
|---|---|
| **Xbim.Presentation** | Библиотека WPF/WinForms-контролов для отображения IFC-моделей (публикуется как NuGet-пакет `Xbim.WindowsUI`) |
| **XbimXplorer** | Основное WPF-приложение — полнофункциональный просмотрщик IFC с 3D-визуализацией, деревом модели, плагинами |
| **XplorerPlugin.Bcf** | Плагин для обмена данными BCF (BIM Collaboration Format) |
| **Xbim.WinformsSample** | Пример интеграции контролов Xbim в приложение Windows Forms |
| **Tests** | Юнит-тесты Xbim.Presentation (MSTest) |

## Ключевые каталоги

### Xbim.Presentation — библиотека контролов
- `DrawingControl3D.xaml` — основной 3D-контрол для визуализации геометрии IFC
- `IfcMetaDataControl.xaml` — отображение метаданных и свойств объектов
- `FederatedModel/` — работа с федеративными (объединёнными) моделями
- `LayerStyling/` — стратегии окраски и стилизации слоёв модели
- `ModelGeomInfo/` — информация о геометрии модели
- `Overlay/` — оверлейные элементы поверх 3D-сцены
- `XplorerPluginSystem/` — система плагинов для расширения функциональности

### XbimXplorer — приложение-просмотрщик
- `XplorerMainWindow.xaml` — главное окно (AvalonDock-докинг)
- `Commands/` — панель команд (ветка дерева модели, поиск, экспорт)
- `Dialogs/` — диалоговые окна
- `PluginSystem/` — загрузка и управление плагинами
- `LogViewer/` — просмотр логов
- `Simplify/` — упрощение геометрии для больших моделей

### XplorerPlugin.Bcf — BCF-плагин
- `UI/` — элементы управления BCF
- `Examples/` — примеры BCF
- `xsd/` — XSD-схемы BCF

## Сборка и запуск

```bash
# Восстановление пакетов и сборка (Release)
dotnet restore Xbim.WindowsUI.sln
msbuild Xbim.WindowsUI.sln /p:Configuration=Release /restore

# Запуск просмотрщика
Output\Release\net472\XbimXplorer.exe
```

Требуется Visual Studio 2022 (MSBuild) или .NET SDK с поддержкой net472.

## Зависимости

- **Xbim.Common / Xbim.Ifc / Xbim.Ifc2x3 / Xbim.Ifc4** 6.1.605 — ядро XBIM (IFC-модель)
- **Xbim.Geometry** 5.1.820 — геометрическое ядро (triangulation, boolean ops)
- **Xbim.IO.Esent / Xbim.IO.MemoryModel** — хранение моделей (Esent / memory)
- **HelixToolkit.Wpf** 2.27.3 — 3D-рендеринг WPF
- **AvalonDock** 2.0 — докинг-панели
- **Serilog** + Microsoft.Extensions.Logging — логирование

## Лицензия

CDDL-1.0 — см. [LICENCE.md](LICENCE.md)
