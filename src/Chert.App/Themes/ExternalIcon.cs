using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chert.App.Services;
using Chert.Core.Utils;

namespace Chert.App.Themes;

/// <summary>
/// 外联图标控件：先显示 PNG 占位（<see cref="IconImage"/> 按 <see cref="FallbackToken"/> 从内嵌资源取），
/// 再异步从 <see cref="Url"/> 加载真实封面图，加载完成后替换占位；任意失败保留占位。
/// 封面文件走 <see cref="IconCache"/> 落盘复用。
/// <para>用法：<c>&lt;themes:ExternalIcon Url="{Binding IconUrl}" FallbackToken="pack" /&gt;</c></para>
/// <para>这是「外联 icon 文件」在 UI 层的统一入口——未来接入皮肤、画廊图等外部图像时，沿用本控件即可。</para>
/// </summary>
public class ExternalIcon : ContentControl
{
    /// <summary>外部图像 URL（空则不加载，仅显示占位）。</summary>
    public static readonly DependencyProperty UrlProperty =
        DependencyProperty.Register(nameof(Url), typeof(string), typeof(ExternalIcon),
            new PropertyMetadata(null, OnUrlChanged));

    /// <summary>占位图标 token（对应内嵌 PNG 文件名，未知则显示空白占位）。</summary>
    public static readonly DependencyProperty FallbackTokenProperty =
        DependencyProperty.Register(nameof(FallbackToken), typeof(string), typeof(ExternalIcon),
            new PropertyMetadata("image", OnFallbackChanged));

    /// <summary>圆角半径（用于裁剪封面与占位，使其贴合卡片圆角）。</summary>
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(ExternalIcon),
            new PropertyMetadata(8.0, OnCornerRadiusChanged));

    public string? Url
    {
        get => (string?)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    public string FallbackToken
    {
        get => (string)GetValue(FallbackTokenProperty);
        set => SetValue(FallbackTokenProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private readonly Image _fallback;
    private CancellationTokenSource? _cts;

    public ExternalIcon()
    {
        _fallback = new Image
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        RefreshFallback();
        Content = _fallback;

        Clip = NewClip();
        SizeChanged += (_, _) => Clip = NewClip();
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => _cts?.Cancel();
    }

    private Geometry? NewClip() =>
        ActualWidth <= 0 || ActualHeight <= 0
            ? null
            : new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), CornerRadius, CornerRadius);

    private static void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ExternalIcon)d).Refresh();

    private static void OnFallbackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ExternalIcon)d).RefreshFallback();

    private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (ExternalIcon)d;
        self.Clip = self.NewClip();
    }

    private void RefreshFallback() => _fallback.Source = IconImage.Get(FallbackToken, 30);

    private void Refresh()
    {
        _cts?.Cancel();
        Content = _fallback;

        var url = Url;
        if (string.IsNullOrWhiteSpace(url))
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var client = LauncherService.Instance.ApiClient;
        var captured = url;

        _ = Task.Run(async () =>
        {
            var path = await IconCache.GetOrDownloadAsync(captured, client, token).ConfigureAwait(false);
            if (path is null || token.IsCancellationRequested)
                return;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.DecodePixelWidth = 256;
                bitmap.EndInit();
                bitmap.Freeze();

                Dispatcher.Invoke(() =>
                {
                    var img = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.UniformToFill,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        SnapsToDevicePixels = true,
                        UseLayoutRounding = true
                    };
                    Content = img;
                });
            }
            catch
            {
                // 保留 PNG 占位
            }
        }, token);
    }
}
