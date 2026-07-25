// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace HostDeviceControl.App.Controls;

/// <summary>
/// Renders a bounded snapshot of telemetry samples without owning acquisition
/// or transport work.
/// </summary>
public sealed class WaveformControl : FrameworkElement
{
    private const int VerticalGridDivisions = 10;
    private const int HorizontalGridDivisions = 8;
    private const double GridLineThickness = 1.0;
    private const double WaveformLineThickness = 1.5;

    private static readonly Brush BackgroundBrush;
    private static readonly Pen GridPen;
    private static readonly Pen WaveformPen;

    static WaveformControl()
    {
        BackgroundBrush = CreateFrozenBrush(Color.FromRgb(16, 24, 32));
        GridPen = CreateFrozenPen(
            Color.FromArgb(70, 180, 190, 200),
            GridLineThickness);
        WaveformPen = CreateFrozenPen(
            Color.FromRgb(64, 220, 160),
            WaveformLineThickness);
    }

    public static readonly DependencyProperty SamplesProperty =
        DependencyProperty.Register(
            nameof(Samples),
            typeof(IReadOnlyList<double>),
            typeof(WaveformControl),
            new FrameworkPropertyMetadata(
                Array.Empty<double>(),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumValueProperty =
        DependencyProperty.Register(
            nameof(MinimumValue),
            typeof(double),
            typeof(WaveformControl),
            new FrameworkPropertyMetadata(
                -1.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumValueProperty =
        DependencyProperty.Register(
            nameof(MaximumValue),
            typeof(double),
            typeof(WaveformControl),
            new FrameworkPropertyMetadata(
                1.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Gets or sets the immutable sample snapshot rendered on the UI thread.
    /// </summary>
    public IReadOnlyList<double> Samples
    {
        get => (IReadOnlyList<double>)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    /// <summary>
    /// Gets or sets the lower display bound.
    /// </summary>
    public double MinimumValue
    {
        get => (double)GetValue(MinimumValueProperty);
        set => SetValue(MinimumValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the upper display bound.
    /// </summary>
    public double MaximumValue
    {
        get => (double)GetValue(MaximumValueProperty);
        set => SetValue(MaximumValueProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        Rect bounds = new(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRectangle(BackgroundBrush, null, bounds);
        DrawGrid(drawingContext, bounds);
        DrawWaveform(drawingContext, bounds);
    }

    private static void DrawGrid(DrawingContext drawingContext, Rect bounds)
    {
        for (int index = 1; index < VerticalGridDivisions; index++)
        {
            double x = bounds.Width * index / VerticalGridDivisions;
            drawingContext.DrawLine(
                GridPen,
                new Point(x, 0),
                new Point(x, bounds.Height));
        }

        for (int index = 1; index < HorizontalGridDivisions; index++)
        {
            double y = bounds.Height * index / HorizontalGridDivisions;
            drawingContext.DrawLine(
                GridPen,
                new Point(0, y),
                new Point(bounds.Width, y));
        }
    }

    private void DrawWaveform(DrawingContext drawingContext, Rect bounds)
    {
        IReadOnlyList<double> samples = Samples;
        if ((samples.Count < 2) ||
            (bounds.Width <= 1) ||
            (bounds.Height <= 1) ||
            (MaximumValue <= MinimumValue))
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            double firstY = ConvertY(samples[0], bounds.Height);
            context.BeginFigure(new Point(0, firstY), false, false);

            for (int index = 1; index < samples.Count; index++)
            {
                double x = bounds.Width * index / (samples.Count - 1);
                double y = ConvertY(samples[index], bounds.Height);
                context.LineTo(new Point(x, y), true, false);
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, WaveformPen, geometry);
    }

    private double ConvertY(double value, double height)
    {
        double clamped = Math.Clamp(value, MinimumValue, MaximumValue);
        double normalized =
            (clamped - MinimumValue) / (MaximumValue - MinimumValue);
        return height - (normalized * height);
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
