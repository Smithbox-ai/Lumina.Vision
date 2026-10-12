using Emgu.CV.Aruco;
using Emgu.CV.Util;
using LuminaCalib.Calibration;
using LuminaCalib.Models;
using LuminaCalib.Services;
using LuminaCalib.ViewModels;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class BoardDefaultsTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"LuminaBoardDefaults_{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);
    }

    [Fact]
    public void Defaults_NewSettingsAndGenerator_UseSameScreenPreset()
    {
        var app = new AppSettings();
        var board = new BoardGeneratorSettings();
        using var vm = new BoardGeneratorViewModel(new SettingsService(_settingsPath));

        Assert.Equal(BoardType.ChArUco, app.BoardType);
        Assert.Equal(6, app.PatternWidth);
        Assert.Equal(3, app.PatternHeight);
        Assert.Equal(10f, app.SquareSize);
        Assert.Equal(0.7f, app.MarkerSizeRatio);
        Assert.Equal(CharucoDictionary.Dict4x4_50, app.CharucoDictionary);
        Assert.Equal(1f, app.DetectionScale);
        Assert.Equal(app.PatternWidth, board.SquaresX);
        Assert.Equal(app.PatternHeight, board.SquaresY);
        Assert.Equal(app.SquareSize, board.SquareLength);
        Assert.Equal(app.MarkerSizeRatio, board.MarkerLengthRatio);
        Assert.Equal(app.CharucoDictionary, board.Dictionary);
        Assert.Equal(app.BoardType, vm.BoardType);
        Assert.Equal(board.SquaresX, vm.SquaresX);
        Assert.Equal(board.SquaresY, vm.SquaresY);
        Assert.Equal(board.SquareLength, vm.SquareLength);
        Assert.Equal(board.MarkerLengthRatio, vm.MarkerLengthRatio);
        Assert.Equal(board.Dictionary, vm.Dictionary);
        Assert.Equal(300, board.Dpi);
        Assert.Equal(50, board.Margin);
        Assert.Equal(board.Dpi, vm.Dpi);
        Assert.Equal(board.Margin, vm.Margin);
    }

    [Theory]
    [InlineData(BoardType.ChArUco, 7, 6)]
    [InlineData(BoardType.Chessboard, 8, 7)]
    public void Constructor_SavedBoardSettings_UsesCalibrationDimensions(
        BoardType boardType, int expectedSquaresX, int expectedSquaresY)
    {
        var service = new SettingsService(_settingsPath);
        service.Settings.BoardType = boardType;
        service.Settings.PatternWidth = 7;
        service.Settings.PatternHeight = 6;
        service.Settings.SquareSize = 25f;
        service.Settings.MarkerSizeRatio = 0.65f;
        service.Settings.CharucoDictionary = CharucoDictionary.Dict5x5_100;
        service.Save();

        using var vm = new BoardGeneratorViewModel(new SettingsService(_settingsPath));

        Assert.Equal(boardType, vm.BoardType);
        Assert.Equal(expectedSquaresX, vm.SquaresX);
        Assert.Equal(expectedSquaresY, vm.SquaresY);
        Assert.Equal(25f, vm.SquareLength);
        Assert.Equal(0.65f, vm.MarkerLengthRatio);
        Assert.Equal(CharucoDictionary.Dict5x5_100, vm.Dictionary);
        Assert.Null(vm.PreviewImage);
    }

    [Fact]
    public void Reset_ModifiedBoardSettings_RestoresSharedScreenPreset()
    {
        var service = new SettingsService(_settingsPath);
        service.Settings.PatternWidth = 17;
        service.Settings.SquareSize = 25f;
        service.Settings.BoardType = BoardType.Chessboard;
        service.Settings.DetectionScale = 0.5f;

        service.Reset();
        using var vm = new BoardGeneratorViewModel(service);

        Assert.Equal(BoardType.ChArUco, service.Settings.BoardType);
        Assert.Equal(6, service.Settings.PatternWidth);
        Assert.Equal(3, service.Settings.PatternHeight);
        Assert.Equal(10f, service.Settings.SquareSize);
        Assert.Equal(1f, service.Settings.DetectionScale);
        Assert.Equal(service.Settings.PatternWidth, vm.SquaresX);
        Assert.Equal(service.Settings.PatternHeight, vm.SquaresY);
        Assert.Equal(service.Settings.SquareSize, vm.SquareLength);
    }

    [Fact]
    public Task GeneratePreview_AfterDispose_CompletesWithoutAccessingResources() => UiTest.Run(async () =>
    {
        using var vm = new BoardGeneratorViewModel(new SettingsService(_settingsPath));
        vm.Dispose();

        await vm.GeneratePreviewCommand.ExecuteAsync(null);

        Assert.Null(vm.PreviewImage);
    });

    [Theory]
    [InlineData(nameof(BoardGeneratorViewModel.IsGenerating))]
    [InlineData(nameof(BoardGeneratorViewModel.PreviewImage))]
    public Task GeneratePreview_DisposedDuringGeneration_DefersResourceRelease(string propertyName) => UiTest.Run(async () =>
    {
        using var vm = new BoardGeneratorViewModel(new SettingsService(_settingsPath));
        bool disposed = false;
        vm.PropertyChanged += (_, args) =>
        {
            if (!disposed && args.PropertyName == propertyName)
            {
                disposed = true;
                vm.Dispose();
            }
        };

        await vm.GeneratePreviewCommand.ExecuteAsync(null);

        Assert.True(disposed);
        Assert.False(vm.IsGenerating);
        Assert.DoesNotContain("Ошибка:", vm.StatusMessage);
        Assert.DoesNotContain("Error:", vm.StatusMessage);
    });

    [Fact]
    public void GenerateCharucoBoard_ScreenPreset_MatchesPixelSpecificationAndDetectsAllCorners()
    {
        var settings = new BoardGeneratorSettings();
        using var generator = new CharucoBoardGenerator();
        using var image = generator.GenerateCharucoBoard(settings);

        Assert.Equal(808, image.Width);
        Assert.Equal(454, image.Height);
        Assert.Equal(settings.BoardWidthPx, image.Width);
        Assert.Equal(settings.BoardHeightPx, image.Height);
        var objects = generator.GetDetectionObjects();
        Assert.NotNull(objects);
        using var parameters = new CharucoParameters(2, false, true);
        using var detector = new CharucoDetector(objects.Value.Board, parameters,
            DetectorParameters.GetDefault(), new RefineParameters(10f, 3f, true));
        using var corners = new VectorOfPointF();
        using var ids = new VectorOfInt();

        EmguCharucoDetection.DetectBoard(detector, image, corners, ids);

        Assert.Equal(10, corners.Size);
        Assert.Equal(Enumerable.Range(0, 10), ids.ToArray().Order());
    }
}
