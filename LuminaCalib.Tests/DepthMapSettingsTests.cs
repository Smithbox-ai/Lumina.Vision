using System.ComponentModel;
using LuminaCalib.Models;
using Xunit;

namespace LuminaCalib.Tests;

public sealed class DepthMapSettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new DepthMapSettings();

        // StereoSGBM
        Assert.Equal(0, settings.MinDisparity);
        Assert.Equal(128, settings.NumDisparities);
        Assert.Equal(5, settings.BlockSize);
        Assert.Equal(0, settings.P1);
        Assert.Equal(0, settings.P2);
        Assert.Equal(1, settings.Disp12MaxDiff);
        Assert.Equal(63, settings.PreFilterCap);
        Assert.Equal(10, settings.UniquenessRatio);
        Assert.Equal(100, settings.SpeckleWindowSize);
        Assert.Equal(2, settings.SpeckleRange);
        Assert.Equal(SgbmMode.Sgbm3Way, settings.SgbmMode);

        // WLS
        Assert.True(settings.UseWlsFilter);
        Assert.Equal(8000.0, settings.WlsLambda);
        Assert.Equal(1.0, settings.WlsSigmaColor);

        // Пост-обработка
        Assert.True(settings.UseMorphologicalClosing);
        Assert.Equal(5, settings.MorphKernelSize);

        // CUDA
        Assert.False(settings.UseCuda);

        // Визуализация
        Assert.Equal(DepthColormap.Turbo, settings.Colormap);
        Assert.Equal(1.0, settings.DisplayScale);
    }

    [Fact]
    public void PropertyChanged_FiresNotification()
    {
        var settings = new DepthMapSettings();
        var changedProperties = new List<string>();
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
                changedProperties.Add(e.PropertyName);
        };

        settings.NumDisparities = 64;
        settings.BlockSize = 7;
        settings.UseWlsFilter = false;
        settings.Colormap = DepthColormap.Jet;

        Assert.Contains(nameof(DepthMapSettings.NumDisparities), changedProperties);
        Assert.Contains(nameof(DepthMapSettings.BlockSize), changedProperties);
        Assert.Contains(nameof(DepthMapSettings.UseWlsFilter), changedProperties);
        Assert.Contains(nameof(DepthMapSettings.Colormap), changedProperties);
    }
}
