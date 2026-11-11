using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace voltrex_quest.Controls;

public sealed partial class GameCoverImage : UserControl
{
    public static readonly DependencyProperty ImageUrlProperty =
        DependencyProperty.Register(
            nameof(ImageUrl),
            typeof(string),
            typeof(GameCoverImage),
            new PropertyMetadata(null, OnImageUrlChanged));

    public static readonly DependencyProperty FallbackInitialsProperty =
        DependencyProperty.Register(
            nameof(FallbackInitials),
            typeof(string),
            typeof(GameCoverImage),
            new PropertyMetadata("?"));

    public new static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(GameCoverImage),
            new PropertyMetadata(new CornerRadius(6)));

    public GameCoverImage()
    {
        InitializeComponent();
    }

    public string? ImageUrl
    {
        get => (string?)GetValue(ImageUrlProperty);
        set => SetValue(ImageUrlProperty, value);
    }

    public string FallbackInitials
    {
        get => (string)GetValue(FallbackInitialsProperty);
        set => SetValue(FallbackInitialsProperty, value);
    }

    public new CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private static void OnImageUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GameCoverImage control)
        {
            control.UpdateImageSource(e.NewValue as string);
        }
    }

    private void UpdateImageSource(string? url)
    {
        CoverImage.Opacity = 0;
        FallbackPanel.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            CoverImage.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage(uri)
            {
                DecodePixelWidth = 360
            };
            CoverImage.Source = bitmap;
        }
        catch
        {
            CoverImage.Source = null;
        }
    }

    private void CoverImage_ImageOpened(object sender, RoutedEventArgs e)
    {
        CoverImage.Opacity = 1;
        FallbackPanel.Visibility = Visibility.Collapsed;
    }

    private void CoverImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        CoverImage.Opacity = 0;
        CoverImage.Source = null;
        FallbackPanel.Visibility = Visibility.Visible;
    }
}

