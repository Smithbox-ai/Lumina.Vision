# Contributing to LuminaCalib

Thank you for your interest in contributing! This document explains how to participate in the development of LuminaCalib.

---

## Ways to Contribute

- **Report a bug** — open a [bug report](../../issues/new?template=bug_report.yml)
- **Suggest a feature** — open a [feature request](../../issues/new?template=feature_request.yml)
- **Submit a pull request** — see the workflow below

---

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- An IDE with Avalonia support: [Rider](https://www.jetbrains.com/rider/) or [Visual Studio 2022+](https://visualstudio.microsoft.com/)
- *(Optional)* CUDA toolkit for GPU-accelerated depth map features

### Getting Started

```sh
git clone https://github.com/Smithbox-ai/Lumina.Vision.git
cd Lumina.Vision

dotnet restore
dotnet build
dotnet test
```

---

## Pull Request Workflow

1. **Fork** the repository and create a branch from `main`.
2. Branch naming convention:
   - Bug fix: `fix/short-description`
   - Feature: `feat/short-description`
   - Refactor: `refactor/short-description`
3. Make changes following the code style below.
4. Add or update tests to cover your changes.
5. Ensure `dotnet test` passes locally.
6. Open a PR against `main` and fill in the pull request template.

---

## Code Style

This project uses `.editorconfig` — your IDE should apply it automatically.

Key conventions:

- 4-space indentation, no tabs.
- File-scoped namespaces.
- Nullable reference types enabled — do not add `#nullable disable`.
- Async methods must accept `CancellationToken` where applicable.
- Dispose `Mat` objects promptly; use `MatPool` for high-frequency allocations.
- Do not introduce hardcoded credentials, URLs, or API keys.

---

## Commit Messages

Use conventional commit prefixes:

| Prefix | When to use |
|--------|-------------|
| `feat:` | New feature |
| `fix:` | Bug fix |
| `refactor:` | Behaviour-neutral code change |
| `test:` | Adding or updating tests |
| `chore:` | Build, deps, or tooling changes |
| `docs:` | Documentation changes only |

Example: `fix: correct timestamp tolerance in StereoSynchronizer`

---

## Questions?

Open a [GitHub Discussion](../../discussions) or a general issue.

---

## На русском

Спасибо за интерес к проекту!

### Способы участия

- **Сообщить об ошибке** — [bug report](../../issues/new?template=bug_report.yml)
- **Предложить функцию** — [feature request](../../issues/new?template=feature_request.yml)
- **Pull Request** — следуйте инструкциям выше

### Подготовка окружения

```sh
git clone https://github.com/Smithbox-ai/Lumina.Vision.git
cd Lumina.Vision

dotnet restore
dotnet build
dotnet test
```

### Стиль кода

Проект использует `.editorconfig`. Основные правила:

- Отступы: 4 пробела.
- File-scoped пространства имён.
- Nullable reference types включены — не добавляйте `#nullable disable`.
- Освобождайте `Mat`-объекты явно; используйте `MatPool` при частых выделениях.
- Не добавляйте захардкоженные учётные данные, URL или ключи API.

### Коммиты

Используйте префиксы: `feat:`, `fix:`, `refactor:`, `test:`, `chore:`, `docs:`.
