# Repository Guidelines

## Project Structure & Module Organization

LuminaCalib is a .NET 10 stereo camera calibration application using Avalonia, CommunityToolkit.Mvvm, and EmguCV. Application source lives in `LuminaCalib/`: `Calibration/` handles calibration and rectification; `Devices/` manages camera capture; `Services/` coordinates synchronization and application services. Keep data in `Models/`, presentation logic in `ViewModels/`, and Avalonia markup in `Views/`, `Controls/`, and `Styles/`. Static resources belong in `Assets/`. Tests live in `LuminaCalib.Tests/`; architecture documentation is in `LuminaCalib/doc/developer-guide.md`. GitHub workflows live in `.github/workflows/`.

## Build, Test, and Development Commands

Install the .NET 10 SDK and run commands from the repository root:

```sh
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --verbosity normal
dotnet run --project LuminaCalib
```

These commands restore packages, build the solution, execute tests against the Release build, and launch the desktop application. For a self-contained package, use `dotnet publish LuminaCalib/LuminaCalib.csproj -c Release -r win-x64 --self-contained true`; use `linux-x64` for Linux. Validate native EmguCV dependencies on the target platform.

## Coding Style & Naming Conventions

Follow `.editorconfig`: UTF-8, CRLF, four spaces for C#, and two spaces for XML, AXAML, and YAML. Prefer file-scoped namespaces and retain nullable reference types. Use PascalCase for types and public members, camelCase for parameters and locals, and descriptive names matching the surrounding code. Support `CancellationToken` in asynchronous operations where applicable. Dispose EmguCV `Mat` objects promptly and use `MatPool` for frequent allocations. Keep camera and calibration logic outside view code.

## Testing Guidelines

Tests use xUnit 4 and Microsoft.Testing.Platform, configured in `global.json`. UI tests use `UiTest.Run` to dispatch through Avalonia.Headless; the Avalonia xUnit v3 bridge is incompatible with xUnit 4. Add regression tests for changed behavior and use names such as `Method_Scenario_ExpectedResult`. Run `dotnet test` for the full suite. Report hardware-dependent checks separately from automated results. No numerical coverage threshold is specified.

## Commit & Pull Request Guidelines

History uses Conventional Commit prefixes, especially `chore:`. Follow `CONTRIBUTING.md` with `feat:`, `fix:`, `refactor:`, `test:`, `chore:`, or `docs:`. Keep commits focused. Open PRs against `main`, complete the PR template, explain the behavior change, reference relevant issues, and record build/test results. Include screenshots for visual changes. Never commit credentials, RTSP passwords, or API keys.
