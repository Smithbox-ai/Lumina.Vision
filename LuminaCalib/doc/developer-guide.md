# Руководство разработчика — LuminaCalib

> **Версия документа:** 1.0  
> **Дата:** 2026-02-10  
> **Проект:** LuminaCalib — приложение стерео-калибровки камер

---

## Содержание

1. [Введение](#1-введение)
2. [Архитектура приложения](#2-архитектура-приложения)
3. [Конвейер обработки кадров](#3-конвейер-обработки-кадров)
4. [Процесс калибровки](#4-процесс-калибровки)
5. [Автозахват (AutoCaptureService)](#5-автозахват-autocaptureservice)
6. [Управление сессиями захвата](#6-управление-сессиями-захвата)
7. [Генератор калибровочных досок](#7-генератор-калибровочных-досок)
8. [Карта глубины (Depth Map)](#8-карта-глубины-depth-map)
9. [Настройки приложения](#9-настройки-приложения)
10. [Локализация](#10-локализация)
11. [UI и темизация](#11-ui-и-темизация)
12. [Обработка ошибок](#12-обработка-ошибок)
13. [Тестирование](#13-тестирование)
14. [Сборка и запуск](#14-сборка-и-запуск)
15. [Структура файлов проекта](#15-структура-файлов-проекта)

---

## 1. Введение

### 1.1. Назначение

**LuminaCalib** — десктопное приложение для стерео-калибровки камер, построенное на Avalonia UI. Предназначено для замены устаревшего инструмента `CalibrationUtility` (WinForms), решая ключевые проблемы предшественника: рассинхронизацию кадров, неудобный UX, отсутствие автозахвата.

### 1.2. Ключевые возможности

- **Стерео-калибровка** с синхронизацией кадров по таймстампам (допуск 20 мс)
- **Три режима калибровки**: Auto (двухэтапная), StereoOnly (с предварительно загруженными внутренними параметрами), SingleCamera (только внутренние параметры)
- **Автозахват** стерео-пар при стабильном обнаружении паттерна
- **Генератор калибровочных досок** ChArUco/Chessboard (PNG/PDF/SVG)
- **Библиотека калибровок** с файловым наблюдателем
- **Управление сессиями захвата** с сохранением пар на диск
- **Real-time ректификация** с переключением вкл/выкл
- **Двуязычный интерфейс** (русский/английский)
- **Карта глубины (Depth Map)** — построение и визуализация карты диспаратности через StereoSGBM + WLS-фильтрацию, три пресета (Fast/Balanced/Quality), экспорт снимков (PNG/TIFF) и облаков 3D-точек (PLY), CUDA-ускорение

### 1.3. Технологический стек

| Технология | Версия | Назначение |
|---|---|---|
| .NET | 10.0 | Целевой фреймворк |
| Avalonia UI | 11.3.11 | Кросс-платформенный UI фреймворк |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM source generators |
| Emgu.CV | 4.12.0.5764 | Обёртка OpenCV для .NET |
| QuestPDF | 2025.12.4 | Генерация PDF-документов |
| Svg.Skia | 3.4.1 | Рендеринг SVG |
| MS.Extensions.DI | 10.0.2 | Dependency Injection |

### 1.4. Требования к окружению

- **.NET 10 SDK** для сборки
- **Windows** или **Linux (Ubuntu 24.04+)** для запуска
- Камеры с поддержкой **RTSP** или **HTTP** потока
- Рекомендуется видеокарта с поддержкой **NVDEC/DXVA2** для аппаратного декодирования

---

## 2. Архитектура приложения

### 2.1. Общая схема

Приложение построено по паттерну **MVVM** (Model-View-ViewModel) с конвенционным маппингом через `ViewLocator`.

```
┌─────────────────────────────────────────────────────────────────────┐
│                           UI Layer                                   │
│  ┌──────────────┐  ┌───────────────┐  ┌───────────────┐             │
│  │  MainWindow   │  │  SettingsView │  │ CalibManager   │  ...        │
│  └──────┬───────┘  └───────┬───────┘  └───────┬───────┘             │
│         │                  │                  │                       │
│  ┌──────┴──────────────────┴──────────────────┴──────────┐          │
│  │                    ViewLocator                         │          │
│  └──────────────────────┬────────────────────────────────┘          │
├─────────────────────────┼───────────────────────────────────────────┤
│                    ViewModel Layer                                    │
│  ┌──────────────────────┴────────────────────────────────┐          │
│  │              MainWindowViewModel                       │          │
│  │  ┌──────────────┐  ┌───────────┐  ┌──────────────┐   │          │
│  │  │SettingsVM    │  │BoardGenVM │  │DepthMapVM    │   │          │
│  │  └──────────────┘  └───────────┘  └──────────────┘   │          │
│  │  ┌──────────────────────────────────────────────┐     │          │
│  │  │        CalibrationManagerVM                   │     │          │
│  │  └──────────────────────────────────────────────┘     │          │
│  └───────────────────────────────────────────────────────┘          │
├─────────────────────────────────────────────────────────────────────┤
│                    Service Layer                                     │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐            │
│  │SettingsService│ │ MatPool      │ │ StereoSynchronizer│            │
│  └──────────────┘ └──────────────┘ └──────────────────┘            │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐            │
│  │AutoCaptureSvc│ │CaptureSession│ │ FileWatcherSvc   │            │
│  └──────────────┘ └──────────────┘ └──────────────────┘            │
│  ┌──────────────┐                                                  │
│  │DepthMapSvc   │                                                  │
│  └──────────────┘                                                  │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐            │
│  │CharucoBoard  │ │PdfExportSvc  │ │ ExceptionLogger  │            │
│  │Generator     │ │              │ │                  │            │
│  └──────────────┘ └──────────────┘ └──────────────────┘            │
├─────────────────────────────────────────────────────────────────────┤
│                    Calibration Layer                                  │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐            │
│  │CalibEngine   │ │CornerDetector│ │StereoRectifier   │            │
│  └──────────────┘ └──────────────┘ └──────────────────┘            │
│  ┌──────────────┐ ┌──────────────────────────────────┐             │
│  │CalibValidator│ │CalibrationHelpers                │             │
│  └──────────────┘ └──────────────────────────────────┘             │
│  ┌──────────────┐                                                  │
│  │DepthMapProc  │                                                  │
│  └──────────────┘                                                  │
├─────────────────────────────────────────────────────────────────────┤
│                    Device Layer                                      │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐            │
│  │ICameraSource │ │EmguCameraSrc │ │AsyncFrameReader  │            │
│  └──────────────┘ └──────────────┘ └──────────────────┘            │
├─────────────────────────────────────────────────────────────────────┤
│                    Model Layer                                       │
│  ┌──────────┐ ┌──────────┐ ┌───────────────┐ ┌────────────┐       │
│  │AppSettings│ │FrameRaw  │ │StereoFramePair│ │CalibResult │       │
│  └──────────┘ └──────────┘ └───────────────┘ └────────────┘       │
│  ┌──────────┐ ┌──────────────────┐ ┌──────────────────────┐       │
│  │BoardGenSettings│ │CaptureSessionManifest│ │CalibLibraryItems    │
│  └──────────┘     └──────────────────┘ └──────────────────────┘   │
│  ┌──────────────────┐                                              │
│  │DepthMapSettings  │                                              │
│  └──────────────────┘                                              │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2. ViewLocator

Конвенционный маппинг ViewModel → View:
- `BoardGeneratorViewModel` → `BoardGeneratorView`
- `CalibrationManagerViewModel` → `CalibrationManagerView`
- `DepthMapViewModel` → `DepthMapView`
- `SettingsViewModel` → `SettingsView`

Реализован в файле `ViewLocator.cs` через замену подстроки `"ViewModel"` → `"View"` в полном имени типа, затем рефлексия для нахождения типа View.

### 2.3. Dependency Injection

Приложение использует `Microsoft.Extensions.DependencyInjection`. Регистрация сервисов происходит в `App.axaml.cs` → `OnFrameworkInitializationCompleted()`:

```
SettingsService (singleton)
   → StereoSynchronizer (получает syncToleranceMs из настроек)
   → CaptureSessionService (получает путь из настроек)
   → MainWindowViewModel (получает все зависимости)
      → SettingsViewModel
      → BoardGeneratorViewModel
      → CalibrationManagerViewModel
```

### 2.4. Слой Models

Модели данных — чистые POCO-классы с поддержкой `INotifyPropertyChanged` через `ObservableObject`:

| Модель | Файл | Назначение |
|---|---|---|
| `AppSettings` | `Models/AppSettings.cs` | Все настройки приложения (камеры, калибровка, язык и т.д.) |
| `FrameRaw` | `Models/FrameRaw.cs` | Кадр камеры: Mat-изображение + таймстамп + FrameId |
| `StereoFramePair` | `Models/StereoFramePair.cs` | Синхронизированная пара L/R кадров |
| `CalibrationResult` | `Models/CalibrationResult.cs` | Результат калибровки: матрицы камер, коэффициенты дисторсии, ротация, трансляция, ректификация |
| `BoardGeneratorSettings` | `Models/BoardGeneratorSettings.cs` | Параметры генерации калибровочной доски |
| `CaptureSessionManifest` | `Models/CaptureSessionManifest.cs` | Метаданные сессии захвата: параметры доски, ссылки на файлы кадров |
| `CalibrationLibraryItem` | `Models/CalibrationLibraryItems.cs` | Элемент каталога калибровок (для UI) |
| `CaptureSessionLibraryItem` | `Models/CalibrationLibraryItems.cs` | Элемент каталога сессий захвата (для UI) |
| `DepthMapSettings` | `Models/DepthMapSettings.cs` | Настройки карты глубины: параметры SGBM, WLS, визуализация, CUDA |

**Перечисления** (определены в `AppSettings.cs`):
- `BoardType` — `Chessboard`, `ChArUco`
- `CharucoDictionary` — 16 вариантов словарей ArUco (4x4..7x7, 50..1000)
- `TransportProtocol` — `Auto`, `UDP`, `TCP`
- `CameraBackend` — `Auto`, `FFmpeg`, `GStreamer`
- `HwAcceleration` — `Auto`, `None`, `NVDEC`, `VAAPI`, `QuickSync`, `DXVA2`
- `CalibrationMode` — `Auto`, `StereoOnly`, `SingleCamera`
- `CalibrationQuality` — `Excellent`, `Good`, `Poor`

### 2.5. Слой Services

| Сервис | Файл | Назначение |
|---|---|---|
| `SettingsService` | `Services/SettingsService.cs` | Загрузка/сохранение настроек в JSON |
| `StereoSynchronizer` | `Services/StereoSynchronizer.cs` | Синхронизация L/R кадров по таймстампам |
| `DepthMapService` | `Services/DepthMapService.cs` | Сервис реального времени для построения карты глубины с backpressure |
| `MatPool` | `Services/MatPool.cs` | Пул Mat-объектов для снижения нагрузки на GC |
| `RingBuffer<T>` | `Services/RingBuffer.cs` | Кольцевой буфер с автоматическим Dispose |
| `AutoCaptureService` | `Services/AutoCaptureService.cs` | Автоматический захват пар при стабильности паттерна |
| `CaptureSessionService` | `Services/CaptureSessionService.cs` | Персистентность сессий захвата на диск |
| `CharucoBoardGenerator` | `Services/CharucoBoardGenerator.cs` | Генерация изображений ChArUco/Chessboard |
| `PdfExportService` | `Services/PdfExportService.cs` | Экспорт доски в PDF |
| `FileWatcherService` | `Services/FileWatcherService.cs` | Наблюдение за папкой CalibrationData |
| `ExceptionLogger` | `Services/ExceptionLogger.cs` | Глобальное логирование исключений |

### 2.6. Слой Calibration

| Класс | Файл | Назначение |
|---|---|---|
| `CalibrationEngine` | `Calibration/CalibrationEngine.cs` | Движок калибровки: одиночная + двухэтапная стерео |
| `CornerDetector` | `Calibration/CornerDetector.cs` | Детекция углов ChArUco/Chessboard |
| `CalibrationValidator` | `Calibration/CalibrationValidator.cs` | Валидация результатов калибровки |
| `StereoRectifier` | `Calibration/StereoRectifier.cs` | Ректификация стерео-изображений |
| `CalibrationHelpers` | `Calibration/CalibrationHelpers.cs` | Вспомогательные методы (фабрики матриц, поиск файлов) |
| `DepthMapProcessor` | `Calibration/DepthMapProcessor.cs` | Конвейер StereoSGBM → WLS → пост-обработка → раскраска + PLY-экспорт |

### 2.7. Слой Devices

| Класс | Файл | Назначение |
|---|---|---|
| `ICameraSource` | `Devices/ICameraSource.cs` | Интерфейс камеры |
| `EmguCameraSource` | `Devices/EmguCameraSource.cs` | RTSP/HTTP камера через EmguCV |
| `AsyncFrameReader` | `Devices/AsyncFrameReader.cs` | Асинхронный ридер с BoundedChannel |

### 2.8. Слой ViewModels

| ViewModel | Файл | Назначение |
|---|---|---|
| `ViewModelBase` | `ViewModels/ViewModelBase.cs` | Базовый класс: `L()`, `RunOnUiThread()` |
| `MainWindowViewModel` | `ViewModels/MainWindowViewModel.cs` | Главная VM: навигация, камеры, калибровка, локализация |
| `SettingsViewModel` | `ViewModels/SettingsViewModel.cs` | Редактор настроек |
| `BoardGeneratorViewModel` | `ViewModels/BoardGeneratorViewModel.cs` | Генератор калибровочных досок |
| `CalibrationManagerViewModel` | `ViewModels/CalibrationManagerViewModel.cs` | Управление библиотекой калибровок и сессий |
| `DepthMapViewModel` | `ViewModels/DepthMapViewModel.cs` | Управление картой глубины: настройки, пресеты, FPS, снимки, экспорт |

### 2.9. Слой Views + Controls

| Компонент | Файл | Назначение |
|---|---|---|
| `MainWindow` | `Views/MainWindow.axaml(.cs)` | Главное окно: навигация, видео, настройки |
| `SettingsView` | `Views/SettingsView.axaml(.cs)` | Панель настроек (3 вкладки) |
| `BoardGeneratorView` | `Views/BoardGeneratorView.axaml(.cs)` | UI генератора досок |
| `CalibrationManagerView` | `Views/CalibrationManagerView.axaml(.cs)` | Менеджер калибровок/сессий |
| `DepthMapView` | `Views/DepthMapView.axaml(.cs)` | UI карты глубины + файловые диалоги (снимки, PLY) |
| `VideoView` | `Controls/VideoView.cs` | High-perf рендеринг видео через WriteableBitmap |
| `WorkflowStepper` | `Controls/WorkflowStepper.cs` | Индикатор шагов калибровки |
| `StatusIndicator` | `Controls/StatusIndicator.cs` | Индикатор подключения камеры + FPS |
| `BoolToColorConverter` | `Controls/BoolToColorConverter.cs` | Конвертер bool → Color |

---

## 3. Конвейер обработки кадров

### 3.1. Общая схема потока данных

```
  Камера (RTSP/HTTP)
       │
       ▼
  ┌────────────────────┐
  │ EmguCameraSource   │  ← VideoCapture.Grab/Retrieve в цикле
  │ (CaptureLoopAsync) │  ← Экспоненциальный backoff при разрыве
  └────────┬───────────┘
           │ OnFrameReceived(FrameRaw)
           ▼
  ┌────────────────────┐
  │ AsyncFrameReader   │  ← BoundedChannel(capacity=2, DropOldest)
  │ (ConsumeFramesAsync)│  ← Backpressure: старые кадры отбрасываются
  └────────┬───────────┘
           │ OnFrameReady(FrameRaw)
           ▼
  ┌────────────────────┐
  │ MainWindowViewModel│  ← EmitLeftFrame / EmitRightFrame
  │ (OnLeftFrameReady) │
  └──────┬─────┬───────┘
         │     │
    ┌────┘     └──────┐
    ▼                 ▼
  ┌──────────┐  ┌──────────────────┐
  │ UI Layer │  │ StereoSynchronizer│ ← RingBuffer<FrameRaw> × 2
  │ VideoView│  │ (PushLeft/Right)  │ ← Bidirectional best-match
  └──────────┘  └────────┬─────────┘
                         │ OnStereoFrameReady(StereoFramePair)
                         ▼
               ┌────────────────────┐
               │ AutoCaptureService │  ← Детекция, стабильность
               │ (ProcessFrame)     │  ← Parallel.Invoke L/R
               └────────┬───────────┘
                         │ OnValidPairCaptured
                         ▼
               ┌────────────────────┐
               │ CalibrationEngine  │  ← Накопление пар, калибровка
               └────────────────────┘
```

### 3.2. Управление памятью Mat

Mat-объекты из EmguCV содержат нативные ресурсы. Для предотвращения утечек и снижения нагрузки на GC применяются три механизма:

#### MatPool (пул объектов)
```
┌─────────────────────────────────────────┐
│              MatPool                     │
│  ConcurrentBag<Mat>                     │
│                                         │
│  Rent() → берёт из пула или создаёт    │
│  Return(mat) → возвращает в пул        │
│                                         │
│  Размер: 1920×1080, CV_8UC3, макс. 20  │
└─────────────────────────────────────────┘
```

#### RingBuffer (кольцевой буфер)
```
Ключевая особенность: при перезаписи элемента вызывается Dispose().

Push(item) → если позиция занята, старый элемент диспозится
             → новый элемент записывается в позицию head
```

#### Паттерн «владения»
- Кто создаёт `FrameRaw.Clone()` — тот и отвечает за `Dispose()`
- `StereoSynchronizer` клонирует кадры для пар → потребитель пары вызывает `Dispose()`
- `AutoCaptureService` передаёт пары через событие → подписчик вызывает `Dispose()`

### 3.3. Синхронизация кадров (StereoSynchronizer)

Алгоритм **двунаправленного поиска лучшего совпадения**:

```
1. Приходит кадр Left:
   a. Кладём в leftBuffer (RingBuffer)
   b. Ищем ближайший Right к latest Left
   c. Ищем ближайший Left к latest Right
   d. Из двух вариантов выбираем пару с меньшим |deltaT|
   e. Если |deltaT| < tolerance (20 мс) → клонируем → OnStereoFrameReady

2. Аналогично для Right

Зачем двунаправленный поиск?
- Камеры могут давать кадры с разной частотой
- Односторонний поиск не гарантирует лучшую пару
- Двунаправленный всегда находит глобальный минимум |deltaT|
```

Допуск (tolerance) настраивается в `AppSettings.SyncToleranceMs` (минимум 20 мс).

---

## 4. Процесс калибровки

### 4.1. Режимы калибровки

| Режим | CalibrationMode | Описание |
|---|---|---|
| **Auto** | `CalibrationMode.Auto` | Двухэтапная: (1) внутренние параметры каждой камеры, (2) внешние параметры стерео-пары с фиксированными внутренними |
| **StereoOnly** | `CalibrationMode.StereoOnly` | Только внешние параметры. Внутренние загружаются из предыдущей калибровки |
| **SingleCamera** | `CalibrationMode.SingleCamera` | Калибровка одной камеры (только внутренние параметры) |

### 4.2. Двухэтапная калибровка (Auto)

Реализована в `CalibrationEngine.CalibrateFullStereo()`:

```
┌─────────────────────────────────────────────────┐
│                 Этап 1: Внутренние               │
│                                                   │
│  ┌──────────────────┐  ┌──────────────────┐      │
│  │ CalibrateCamera  │  │ CalibrateCamera  │      │
│  │ (Left)           │  │ (Right)          │      │
│  │ → CameraMatrix_L │  │ → CameraMatrix_R │      │
│  │ → DistCoeffs_L   │  │ → DistCoeffs_R   │      │
│  └──────────────────┘  └──────────────────┘      │
│         ↓ Parallel.Invoke ↓                       │
├─────────────────────────────────────────────────┤
│                 Этап 2: Внешние                   │
│                                                   │
│  StereoCalibrate(                                │
│    flags: FixIntrinsic  ← ключевой флаг!         │
│  )                                                │
│  → R, T, E, F (вращение, трансляция, ...)         │
│  → reprojectionError                              │
├─────────────────────────────────────────────────┤
│                 Этап 3: Ректификация             │
│                                                   │
│  StereoRectify()                                  │
│  → R1, R2, P1, P2, Q                             │
└─────────────────────────────────────────────────┘
```

**Почему FixIntrinsic?**

Без фиксации внутренних параметров `StereoCalibrate` пытается одновременно оптимизировать и внутренние, и внешние параметры, что приводит к нестабильным результатам при ограниченном числе калиброванных пар. Двухэтапный подход даёт более точные результаты.

### 4.3. Детекция углов

`CornerDetector` поддерживает два типа паттернов:

| Паттерн | Метод | Особенности |
|---|---|---|
| **Chessboard** | `FindChessboardCorners` + `CornerSubPix` | Классический, простой. Углы расположены на сетке без ID |
| **ChArUco** | `DetectMarkers` → `InterpolateCornersCharuco` | Каждый угол имеет уникальный ID → при частичном перекрытии можно использовать только видимые углы |

Преимущество ChArUco: при стерео-калибровке не требуется, чтобы обе камеры видели все углы. `CalibrationEngine.TryBuildStereoPoints` пересекает видимые ID из левого и правого кадров (требуется ≥ 4 общих).

### 4.4. Валидация результатов

`CalibrationValidator.Validate()` проверяет:
- Ошибка репроекции < 2.0 → `IsValid = true`
- Матрицы камер содержат ненулевые фокусные расстояния
- Дисторсия в разумных пределах
- Матрицы ректификации заполнены

Качество (`CalibrationQuality`):
- < 0.5 → `Excellent`
- < 1.0 → `Good`
- ≥ 1.0 → `Poor`

### 4.5. Ректификация

`StereoRectifier` предвычисляет карты ректификации (`InitUndistortRectifyMap`) и применяет их через `CvInvoke.Remap` для real-time трансформации кадров.

Включается переключателем `ShowRectified` в `MainWindowViewModel`. При включении в `VideoView` отображаются ректифицированные кадры с эпиполярными линиями для визуальной проверки.

---

## 5. Автозахват (AutoCaptureService)

### 5.1. Принцип работы

```
┌──────────────────────────────────────────────────────────┐
│                 AutoCaptureService                        │
│                                                           │
│  ProcessFrame(pair) ──────────────────► Parallel.Invoke  │
│                        ┌─────────────────┐ ┌───────────┐ │
│                        │DetectCorners(L) │ │Detect(R)  │ │
│                        └───────┬─────────┘ └─────┬─────┘ │
│                                │                 │        │
│                    foundLeft?  ▼     foundRight? ▼        │
│                                                           │
│  ┌─ Оба найдены? ──────────────────────────────┐         │
│  │   ↓ Да                                       │         │
│  │   Первый раз? → запомнить timestamp          │         │
│  │   Уже был?    → (now - firstSeen) > Threshold│         │
│  │                 ↓ Да                          │         │
│  │                 Прошло MinCaptureInterval?    │         │
│  │                 ↓ Да                          │         │
│  │                 ★ OnValidPairCaptured ★       │         │
│  └──────────────────────────────────────────────┘         │
│                                                           │
│  Параметры:                                               │
│    MinCaptureIntervalMs = 2000 мс                         │
│    StabilityThresholdMs = 1000 мс                         │
│    MaxCaptures          = 20                               │
└──────────────────────────────────────────────────────────┘
```

### 5.2. Backpressure

Используется `Interlocked.CompareExchange` на флаге `_isProcessing`:
- Если предыдущий кадр ещё обрабатывается → новый игнорируется
- Это предотвращает накопление необработанных кадров при медленной детекции

### 5.3. Потокобезопасность

- `ProcessFrame` может вызываться из любого потока
- Внутренний `_lock` защищает состояние стабильности
- События `OnDetectionUpdate` и `OnValidPairCaptured` вызываются из рабочего потока — UI-подписчикам нужно использовать `Dispatcher.UIThread.Post`

---

## 6. Управление сессиями захвата

### 6.1. Структура на диске

```
CaptureSessions/
├── 2026-02-10_14-30_auto_9x6/
│   ├── session.json          ← CaptureSessionManifest
│   └── frames/
│       ├── 001_left.png
│       ├── 001_right.png
│       ├── 002_left.png
│       ├── 002_right.png
│       └── ...
├── 2026-02-10_15-00_stereo-only_9x6/
│   ├── session.json
│   └── frames/
│       └── ...
```

### 6.2. Манифест сессии (session.json)

```json
{
  "SessionId": "2026-02-10_14-30_auto_9x6",
  "CreatedAtUtc": "2026-02-10T11:30:00Z",
  "CaptureMode": "Auto",
  "BoardType": "ChArUco",
  "PatternWidth": 9,
  "PatternHeight": 6,
  "SquareSizeMm": 30.0,
  "Captures": [
    {
      "Index": 1,
      "LeftImageFile": "001_left.png",
      "RightImageFile": "001_right.png",
      "TimeDeltaMs": 12.5,
      "LeftPatternFound": true,
      "RightPatternFound": true
    }
  ]
}
```

### 6.3. CaptureSessionService

Основные операции:
- `StartSession(settings, mode)` → создаёт директорию + манифест
- `AppendCaptureAsync(manifest, pair, leftFound, rightFound)` → сохраняет PNG-и + обновляет манифест
- `LoadSessionAsync(path)` → загружает манифест из файла или директории
- `SaveManifest(manifest)` → сериализует manifest в session.json

### 6.4. Интеграция с калибровкой

При включённом `UseSavedSessionForCalibration`:
1. Вместо live-захвата используется сохранённая сессия
2. `RunCalibrationFromSession` загружает PNG-и, прогоняет `CornerDetector` и подаёт в `CalibrationEngine`
3. Позволяет повторить калибровку с уже собранными кадрами

---

## 7. Генератор калибровочных досок

### 7.1. Поддерживаемые типы

| Тип | Описание | Формат | DPI |
|---|---|---|---|
| **ChArUco** | Шахматная доска + ArUco маркеры | PNG, PDF, SVG | 72–600 |
| **Chessboard** | Классическая шахматная доска | PNG, PDF | 72–600 |

### 7.2. CharucoBoardGenerator

Для ChArUco:
1. Создаёт словарь ArUco (`Dictionary`)
2. Создаёт `CharucoBoard` с заданными параметрами
3. Вызывает `GenerateImage()` → Mat
4. Применяет отступы (margin)

Для Chessboard:
1. Вычисляет размер в пикселях из DPI и размера квадрата
2. Рисует чёрно-белые квадраты через `CvInvoke.Rectangle`
3. Применяет отступы

### 7.3. Экспорт

- **PNG**: `CvInvoke.Imwrite` с уровнем сжатия
- **PDF**: QuestPDF — страница A4 с изображением доски, таблицей спецификаций и физическими размерами
- **Спецификация**: текстовый файл с параметрами доски

### 7.4. BoardGeneratorViewModel

- Предпросмотр обновляется при изменении любого параметра (debounce через `SemaphoreSlim`)
- Mat конвертируется в `WriteableBitmap` для отображения в Avalonia
- Поддерживает сохранение через диалоги Avalonia `FolderPickerOpenOptions`

---

## 8. Карта глубины (Depth Map)

Модуль построения и визуализации карты глубины по калиброванной стереопаре в реальном времени.

### 8.1. Архитектура pipeline

```
  MainWindowViewModel
       │  StereoFramePair (ректифицированные или сырые)
       ▼
  ┌────────────────────┐
  │  DepthMapService   │  ← Backpressure: один кадр за раз
  │  (ProcessStereoFrame) │  ← Throttling: ProcessEveryNthFrame
  └────────┬───────────┘
           │
     ┌─────┴──────┐
     ▼             ▼
  Rectify?    DepthMapProcessor
  (StereoRectifier)    │
           ┌───────────┼───────────┐
           ▼           ▼           ▼
       Grayscale   StereoSGBM   WLS Filter
           │       (или CUDA     (DisparityWLSFilter)
           │        StereoBM)    │
           │           │         ▼
           │           │    Morphological
           │           │      Closing
           │           ▼         │
           │      Raw Disparity  │
           │       (CV_16S)      │
           │           │         │
           ▼           ▼         ▼
       ColorizeDisparity → Colorized Mat (CV_8UC3)
                │
                ▼
        OnDepthMapReady(Mat)
                │
                ▼
        DepthMapViewModel
        (Mat → WriteableBitmap)
                │
                ▼
           DepthMapView
           (Image.Source)
```

Ключевые особенности:
- **Backpressure**: `Interlocked.CompareExchange` гарантирует обработку одного кадра за раз
- **Throttling**: параметр `ProcessEveryNthFrame` позволяет пропускать кадры
- **Thread safety**: `_snapshotLock` защищает снимки disparity и rectifiedLeft для экспорта
- **CUDA path**: при `UseCuda && !UseWlsFilter` используется `CudaStereoBM` через GPU

### 8.2. Параметры StereoSGBM

| Параметр | Диапазон | По умолчанию | Описание |
|---|---|---|---|
| `NumDisparities` | 16–512 (×16) | 128 | Диапазон поиска диспаратности |
| `BlockSize` | 3–11 (нечётное) | 5 | Размер блока сопоставления |
| `MinDisparity` | 0–100 | 0 | Минимальное значение диспаратности |
| `P1` | 0–auto | 0 (auto: 8×cn×bs²) | Штраф сглаживания ±1 |
| `P2` | 0–auto | 0 (auto: 32×cn×bs²) | Штраф сглаживания >±1 |
| `Disp12MaxDiff` | -1–10 | 1 | Макс. разница L-R проверки |
| `PreFilterCap` | 1–63 | 63 | Усечение предфильтра |
| `UniquenessRatio` | 0–30 | 10 | Порог уникальности (%) |
| `SpeckleWindowSize` | 0–200 | 100 | Окно фильтра пятен |
| `SpeckleRange` | 0–10 | 2 | Макс. диспаратность пятна |
| `SgbmMode` | Enum | `Sgbm3Way` | Алгоритм: SGBM / HH / Sgbm3Way / HH4 |

### 8.3. WLS-фильтр (Weighted Least Squares)

WLS-фильтр (`DisparityWLSFilter`) значительно улучшает качество карты глубины:
- Сглаживает «шум» в однородных областях
- Сохраняет чёткие границы объектов
- Заполняет «дыры» в окклюзиях

Параметры:
- `WlsLambda` (по умолчанию 8000.0) — сила сглаживания. Бо́льшие значения → более гладкий результат
- `WlsSigmaColor` (по умолчанию 1.5) — чувствительность к цветовым границам. Меньшие значения → больше сглаживания через границы

Для WLS создаётся `RightMatcher` через `StereoMatcherCreate.CreateRightMatcher`, а фильтр — через `StereoMatcherCreate.CreateDisparityWLSFilter`.

**Ограничение**: WLS несовместим с CUDA path — при включённом WLS всегда используется CPU.

### 8.4. CUDA-ускорение

При наличии NVIDIA GPU и `UseCuda = true` (при выключенном WLS):
1. Проверяется `CudaInvoke.HasCuda`
2. Изображения загружаются в `GpuMat`
3. Используется `CudaStereoBM` (вместо StereoSGBM)
4. Результат скачивается обратно в CPU `Mat`

При любой ошибке CUDA автоматически срабатывает fallback на CPU pipeline.

### 8.5. Пресеты

| Пресет | NumDisp | Block | WLS | Scale | Описание |
|---|---|---|---|---|---|
| **Fast** | 64 | 9 | Выкл | 0.5× | Минимальная задержка, подходит для слабых CPU |
| **Balanced** | 128 | 5 | Вкл | 1.0× | Оптимальный баланс качества и скорости |
| **Quality** | 256 | 5 | Вкл, λ=80000 | 1.0× | Максимальное качество, высокие требования к CPU |

### 8.6. Экспорт

#### Снимки (Save Snapshot)
- **PNG** — раскрашенная карта глубины (colorized, CV_8UC3)
- **TIFF 16-bit** — сырая диспаратность (CV_16S), масштаб ×16

Имя файла: `depthmap_YYYY-MM-DD_HH-mm-ss.{png,tiff}`

#### Облако 3D-точек (Export Point Cloud)
1. `CvInvoke.ReprojectImageTo3D` преобразует disparity → 3D точки через Q-матрицу
2. `DepthMapProcessor.ExportPly` экспортирует в ASCII PLY-формат
3. Фильтрация: отсеиваются точки с z ≤ 0, z > maxDepth (10 000) или z = ∞
4. Цвет: BGR → RGB из ректифицированного левого кадра
5. Формат: UTF-8, LF, `CultureInfo.InvariantCulture` для float

### 8.7. Troubleshooting

| Проблема | Причина | Решение |
|---|---|---|
| Чёрная карта глубины | Нет загруженной калибровки | Выполнить калибровку и загрузить результат |
| Шумная / полосатая карта | Плохая калибровка (error > 1.0) | Перекалибровать с ≥ 15 парами, проверить ChArUco |
| Низкий FPS | Высокие настройки + слабый CPU | Использовать пресет Fast или уменьшить DisplayScale |
| WLS не работает с CUDA | Архитектурное ограничение | Отключить CUDA или отключить WLS |
| PLY-файл пустой | Все точки отфильтрованы (z > maxDepth) | Проверить калибровку и Q-матрицу |

---

## 9. Настройки приложения

### 9.1. SettingsService

Настройки хранятся в `settings.json` рядом с исполняемым файлом. Загрузка ленивая — при первом обращении к `Settings` свойству.

```
settings.json
{
  "Language": "ru",
  "AutoSaveCalibration": true,
  "CalibrationDataPath": "./CalibrationData",
  "CaptureSessionsPath": "./CaptureSessions",
  "LeftCameraUrl": "rtsp://192.168.1.10:554/stream",
  "RightCameraUrl": "rtsp://192.168.1.11:554/stream",
  "BoardType": "ChArUco",
  "PatternWidth": 9,
  "PatternHeight": 6,
  "SquareSize": 30.0,
  "CalibrationMode": "Auto",
  "RequiredFrames": 20,
  "SyncToleranceMs": 50.0,
  ...
}
```

### 9.2. Структура AppSettings

Группы настроек:
- **Общие**: язык, автосохранение, пути к данным
- **Камеры**: URL, логин, пароль для левой и правой камер
- **Расширенные камеры**: транспортный протокол, backend, HW-ускорение, буфер, таймауты, задержки реконнекта
- **Доска**: тип, размер паттерна (`PatternWidth/PatternHeight`), размер квадрата, словарь ArUco
- **Калибровка**: режим, количество кадров, допуск синхронизации, порог стабильности
- **Активные ассеты**: пути к текущей активной калибровке и сессии для каждого режима

Для `BoardType = Chessboard` параметры `PatternWidth/PatternHeight` задаются как количество внутренних углов
(`boardSize.width` / `boardSize.height` в OpenCV).
Для `BoardType = ChArUco` параметры `PatternWidth/PatternHeight` задаются как количество клеток.

### 9.3. SettingsViewModel

Три вкладки:
1. **Общее**: язык, автосохранение, пути
2. **Камеры**: URL/логин/пароль + тест подключения, расширенные параметры
3. **Доска**: тип паттерна, размер сетки, кнопка «Генерировать доску»

---

## 10. Локализация

### 10.1. Механизм L()

Приложение поддерживает русский и английский языки. Локализация реализована через метод `L(string en, string ru)` в `ViewModelBase`:

```csharp
protected static string L(string en, string ru)
{
    var lang = SettingsService.CurrentLanguage;
    return lang == "en" ? en : ru;
}
```

Каждый ViewModel вызывает `UpdateLocalizedUiTexts()` при смене языка, обновляя все binding-свойства:

```csharp
private void UpdateLocalizedUiTexts()
{
    HeaderText = L("Board Generator", "Генератор досок");
    SaveButtonText = L("Save", "Сохранить");
    // ...
}
```

### 10.2. Переключение языка

1. Пользователь меняет язык в настройках
2. `AppSettings.Language` срабатывает `PropertyChanged`
3. `SettingsService` обновляет статическое `CurrentLanguage`
4. Каждый ViewModel подписан на `PropertyChanged` → вызывает `UpdateLocalizedUiTexts()`
5. UI обновляется через binding

### 10.3. MainWindowViewModel — расширенная локализация

`MainWindowViewModel` содержит расширенную версию `L()` с:
- Словарём `RuLocalization` (~80 записей) для fallback-поиска
- Обработкой mojibake (`TryDecodeMojibake`) — защита от ошибок кодировки Windows-1251 → UTF-8

### 10.4. Установка культуры

При старте приложения в `App.axaml.cs` устанавливается культура:
```csharp
Thread.CurrentThread.CurrentCulture = new CultureInfo(lang);
Thread.CurrentThread.CurrentUICulture = new CultureInfo(lang);
```

---

## 11. UI и темизация

### 11.1. Тема «Luxury Industrial»

Тёмная цветовая палитра с неоновыми акцентами:

| Цвет | Hex | Назначение |
|---|---|---|
| Deep Charcoal | `#1E1E1E` | Основной фон |
| Elevated Gray | `#2C2C2C` | Поверхности (панели, карточки) |
| Surface Light | `#3C3C3C` | Кнопки, разделители |
| Neon Cyan | `#00E5FF` | Акцент (активные элементы) |
| Emerald | `#00C853` | Успех (паттерн найден, подключено) |
| Amber | `#FFD600` | Предупреждение |
| Error Red | `#FF5252` | Ошибка (паттерн не найден, разрыв) |
| Text Primary | `#FFFFFF` | Основной текст |
| Text Secondary | `#B0B0B0` | Вторичный текст |

### 11.2. AppStyles.axaml

Определяет:
- Ресурсы цветов и кистей
- Стили для `Window`, `Border.surface`, `Border.elevated`
- Стили кнопок: `Button.primary` (cyan), `Button.secondary` (gray)
- Стили текста: `TextBlock.header/subheader/body/caption`
- Стили индикаторов: `Ellipse.status-connected/disconnected/warning`
- Стили фреймов видео: `Border.video-container`
- Стили шагов: `Border.step-active/complete/pending`

### 11.3. Компоненты

#### VideoView
- Рендеринг видео через `WriteableBitmap` с прямым копированием пикселей (`Buffer.MemoryCopy`)
- Поддерживает overlay рисование обнаруженных углов (Canvas)
- Индикаторы статуса: NoSignal, Connecting, Connected, Error, HoldStill, Captured
- Автоматическое масштабирование при изменении размера окна

#### WorkflowStepper
- 4 шага: Setup → Capture → Calibrate → Save
- Цветовая индикация: зелёный (завершён), cyan (активный), серый (ожидание)
- Программное перестроение UI при изменении шага

#### StatusIndicator
- Точка-индикатор (зелёная/красная)
- FPS в формате `"30.0 fps"` или `"Отключено"`
- Дополнительная информация (reconnect schedule)

---

## 12. Обработка ошибок

### 12.1. ExceptionLogger

Глобальная система логирования с тремя точками перехвата:

```
1. AppDomain.CurrentDomain.UnhandledException
   → необработанные исключения в любом потоке

2. TaskScheduler.UnobservedTaskException
   → незамеченные исключения в Task'ах

3. Dispatcher.UIThread.UnhandledException
   → исключения в UI-потоке Avalonia
```

В `Exceptions.txt` рядом с исполняемым файлом записываются только исключения.
Информационные сообщения (connect/disconnect/capture и т.д.) остаются в UI-вкладке `Log`.
Формат записей в `Exceptions.txt`:
```
========== 2026-02-10 14:30:00 (Context) ==========
System.InvalidOperationException: Сообщение
   at ...
```

### 12.2. Reconnect камер

`EmguCameraSource` реализует автоматическое переподключение с экспоненциальным backoff:

```
Разрыв → ожидание initialDelay → попытка → 
  успех? → продолжить
  неудача? → ожидание delay × 2 (≤ maxDelay) → повторить
```

Параметры по умолчанию:
- `CameraNoFrameTimeoutMs = 1500` — таймаут отсутствия кадров
- `CameraReconnectInitialDelayMs = 1000` — начальная задержка
- `CameraReconnectMaxDelayMs = 10000` — максимальная задержка

### 12.3. Log View

В UI доступна вкладка «Log» с прокручиваемым списком событий:
- Подключение/отключение камер
- Ошибки декодирования
- Захват пар
- Результаты калибровки
- Исключения

Максимум `MaxLogEntries = 300` записей (старые удаляются).

---

## 13. Тестирование

### 13.1. Структура тестов

Проект `LuminaCalib.Tests` содержит юнит-тесты на xUnit:

| Файл | Тестируемый класс | Кол-во тестов |
|---|---|---|
| `MainWindowViewModelTests.cs` | MainWindowViewModel | Навигация, состояние, калибровка |
| `MainWindowWorkflowLocalizationTests.cs` | MainWindowViewModel | Локализация workflow-текстов |
| `SettingsViewModelTests.cs` | SettingsViewModel | Сохранение, сброс, тест камер |
| `BoardGeneratorViewModelTests.cs` | BoardGeneratorViewModel | Генерация, параметры, локализация |
| `CalibrationManagerViewModelTests.cs` | CalibrationManagerViewModel | Управление библиотекой |
| `CalibrationEngineTests.cs` | CalibrationEngine | Добавление пар, калибровка |
| `CalibrationResultTests.cs` | CalibrationResult | Сохранение/загрузка XML |
| `CaptureSessionServiceTests.cs` | CaptureSessionService | Сессии захвата |
| `AutoCaptureServiceTests.cs` | AutoCaptureService | Автозахват, стабильность |
| `StereoSynchronizerTests.cs` | StereoSynchronizer | Синхронизация кадров |
| `ViewLoadingTests.cs` | Views | Загрузка без исключений |
| `DepthMapSettingsTests.cs` | DepthMapSettings | Значения по умолчанию, PropertyChanged |
| `DepthMapProcessorTests.cs` | DepthMapProcessor | Disparity, colorize, CUDA fallback, downscale |
| `DepthMapServiceTests.cs` | DepthMapService | Backpressure, throttling, dispose |
| `DepthMapViewModelTests.cs` | DepthMapViewModel | Пресеты, синхронизация, события |
| `DepthMapExportTests.cs` | DepthMapExportTests | Снимки, PLY-экспорт, фильтрация |

### 13.2. Моки и тестирование

ViewModel-тесты используют реальные сервисы с временными директориями:
```csharp
var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
var settingsService = new SettingsService(Path.Combine(tmpDir, "settings.json"));
var vm = new MainWindowViewModel(settingsService, synchronizer);
```

ViewLoading-тесты наследуют `AvaloniaAppTestBase`, который инициализирует headless Avalonia.

### 13.3. Запуск тестов

```bash
# Все тесты
dotnet test LuminaCalib.Tests/LuminaCalib.Tests.csproj

# С verbose выводом
dotnet test LuminaCalib.Tests/LuminaCalib.Tests.csproj -v m

# Конкретный тест
dotnet test --filter "FullyQualifiedName~MainWindowViewModelTests"
```

---

## 14. Сборка и запуск

### 14.1. Команды сборки

```bash
# Сборка
dotnet build LuminaCalib/LuminaCalib.csproj

# Сборка Release
dotnet build LuminaCalib/LuminaCalib.csproj -c Release

# Запуск
dotnet run --project LuminaCalib/LuminaCalib.csproj

# Публикация (Windows, self-contained)
dotnet publish LuminaCalib/LuminaCalib.csproj -c Release -r win-x64 --self-contained
```

### 14.2. Кросс-платформенность

Проект поддерживает Windows и Linux (Ubuntu 24.04+). Условные пакеты EmguCV:

```xml
<PackageReference Include="Emgu.CV.runtime.windows" Version="5.0.0.6584"
    Condition="$([MSBuild]::IsOSPlatform('Windows'))" />
<PackageReference Include="Emgu.CV.runtime.ubuntu-24.04-x64" Version="5.0.0.6584"
    Condition="$([MSBuild]::IsOSPlatform('Linux'))" />
```

### 14.3. Unsafe blocks

Проект использует `AllowUnsafeBlocks = true` для прямого копирования пикселей в `VideoView` через `Buffer.MemoryCopy` — это необходимо для высокопроизводительного рендеринга видео.

### 14.4. Compiled Bindings

Включён режим `AvaloniaUseCompiledBindingsByDefault = true` для проверки привязок на этапе компиляции.

---

## 15. Структура файлов проекта

```
LuminaCalib/
│
├── App.axaml                    # Определение приложения Avalonia (тема, стили)
├── App.axaml.cs                 # DI, культура, обработчики исключений
├── app.manifest                 # Манифест Windows
├── LuminaCalib.csproj           # Файл проекта (.NET 10, WinExe)
├── Program.cs                   # Точка входа, Avalonia builder
├── ViewLocator.cs               # ViewModel → View маппинг (конвенция)
├── PROJECT_SUMMARY.md           # Дизайн-документ (рус.)
│
├── Assets/
│   └── avalonia-logo.ico        # Иконка приложения
│
├── Calibration/                 # Ядро калибровки
│   ├── CalibrationEngine.cs     # Движок: одиночная + стерео калибровка
│   ├── CalibrationHelpers.cs    # Утилиты: фабрики матриц, поиск файлов
│   ├── CalibrationValidator.cs  # Валидация результатов
│   ├── CornerDetector.cs        # Детекция углов ChArUco/Chessboard
│   ├── StereoRectifier.cs       # Ректификация изображений
│   └── DepthMapProcessor.cs     # Конвейер SGBM → WLS → колоризация + PLY-экспорт
│
├── Controls/                    # Кастомные UI-контролы
│   ├── BoolToColorConverter.cs  # IValueConverter: bool → зелёный/красный
│   ├── StatusIndicator.cs       # Индикатор камеры (точка + FPS)
│   ├── VideoView.cs             # WriteableBitmap-рендеринг видео + overlay
│   └── WorkflowStepper.cs       # 4-шаговый индикатор progress
│
├── Devices/                     # Работа с камерами
│   ├── ICameraSource.cs         # Интерфейс камеры
│   ├── AsyncFrameReader.cs      # BoundedChannel-ридер с backpressure
│   └── EmguCameraSource.cs      # RTSP/HTTP камера через EmguCV
│
├── doc/                         # Документация
│   └── developer-guide.md       # Это руководство
│
├── Models/                      # Модели данных
│   ├── AppSettings.cs           # Настройки + все enum-ы
│   ├── BoardGeneratorSettings.cs# Параметры генератора досок
│   ├── CalibrationLibraryItems.cs # UI-модели каталога калибровок
│   ├── CalibrationResult.cs     # Результат калибровки (Mat-ы, ошибки)
│   ├── CaptureSessionManifest.cs# Манифест сессии захвата
│   ├── DepthMapSettings.cs      # Настройки карты глубины (SGBM, WLS, CUDA)
│   ├── FrameRaw.cs              # Кадр: Mat + таймстамп + ID
│   └── StereoFramePair.cs       # Синхронизированная пара L/R
│
├── Services/                    # Сервисы бизнес-логики
│   ├── AutoCaptureService.cs    # Автозахват по стабильности паттерна
│   ├── CaptureSessionService.cs # Персистентность сессий на диск
│   ├── DepthMapService.cs       # Сервис карты глубины с backpressure
│   ├── CharucoBoardGenerator.cs # Генерация ChArUco/Chessboard досок
│   ├── ExceptionLogger.cs       # Глобальный логер исключений
│   ├── FileWatcherService.cs    # FileSystemWatcher для CalibrationData
│   ├── MatPool.cs               # Пул Mat-объектов
│   ├── PdfExportService.cs      # PDF-экспорт через QuestPDF
│   ├── RingBuffer.cs            # Кольцевой буфер с dispose-on-overwrite
│   ├── SettingsService.cs       # JSON-настройки (загрузка/сохранение)
│   └── StereoSynchronizer.cs    # Синхронизация L/R кадров
│
├── Styles/
│   └── AppStyles.axaml          # «Luxury Industrial» тема
│
├── ViewModels/                  # MVVM ViewModels
│   ├── ViewModelBase.cs         # Базовый: L(), RunOnUiThread()
│   ├── MainWindowViewModel.cs   # Главная VM (~2700 строк)
│   ├── SettingsViewModel.cs     # Редактор настроек
│   ├── BoardGeneratorViewModel.cs # Генератор досок
│   ├── CalibrationManagerViewModel.cs # Менеджер библиотеки
│   └── DepthMapViewModel.cs     # Карта глубины: настройки, пресеты, FPS, экспорт
│
└── Views/                       # MVVM Views (AXAML + code-behind)
    ├── MainWindow.axaml(.cs)    # Главное окно + видео + навигация
    ├── SettingsView.axaml(.cs)  # Панель настроек (3 вкладки)
    ├── BoardGeneratorView.axaml(.cs) # UI генератора
    ├── CalibrationManagerView.axaml(.cs) # Менеджер калибровок
    └── DepthMapView.axaml(.cs)  # UI карты глубины + файловые диалоги
```

---

## Приложение А. Глоссарий

| Термин | Описание |
|---|---|
| **Intrinsics** | Внутренние параметры камеры: фокусное расстояние, главная точка, дисторсия |
| **Extrinsics** | Внешние параметры: вращение (R) и трансляция (T) между камерами |
| **ChArUco** | Комбинация шахматной доски и маркеров ArUco. Каждый угол имеет уникальный ID |
| **Ректификация** | Трансформация изображений для выравнивания эпиполярных линий |
| **Эпиполярные линии** | Горизонтальные линии, на которых лежат соответствующие точки двух камер |
| **Ошибка репроекции** | Среднее расстояние между реальными и предсказанными положениями точек |
| **FixIntrinsic** | Флаг StereoCalibrate — фиксирует внутренние параметры, калибрует только внешние |
| **Backpressure** | Механизм контроля потока: отбрасывание старых данных при невозможности обработки новых |
| **Disparity** | Разница горизонтальных позиций точки в левом и правом изображении. Обратно пропорциональна глубине |
| **StereoSGBM** | Semi-Global Block Matching — алгоритм вычисления карты диспаратности |
| **WLS-фильтр** | Weighted Least Squares — фильтр сглаживания диспаратности с сохранением границ |
| **PLY** | Polygon File Format — формат хранения 3D-облаков точек (вершины + цвет) |
| **Q-матрица** | Матрица 4×4 для преобразования диспаратности в 3D-координаты (из StereoRectify) |
| **CUDA** | Compute Unified Device Architecture — технология NVIDIA для вычислений на GPU |
| **Mojibake** | Некорректное отображение символов при ошибке кодировки |

---

*Конец документа.*
