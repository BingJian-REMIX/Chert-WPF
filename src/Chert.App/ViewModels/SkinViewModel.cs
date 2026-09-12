using System.IO;
using System.Net.Http;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Chert.Core.Mvvm;
using Chert.Core.Skin;
using Chert.Core.Utils;

namespace Chert.App.ViewModels;

/// <summary>皮肤预览页 ViewModel（规格 2.3 皮肤编辑器 / 规格 3.8 皮肤预览）。
/// 除 2D 正版皮肤 URL 外，额外下载位图供 3D 角色预览使用，并暴露 Slim 标志区分 classic / slim 模型。</summary>
public class SkinViewModel : ObservableObject
{
    private string _playerName = "";
    private string _skinUrl = "";
    private BitmapImage? _skinImage;
    private string _modelType = "classic";
    private string _statusMessage = "";
    private bool _isBusy;
    private bool _hasSkin;

    public string PlayerName
    {
        get => _playerName;
        set => SetField(ref _playerName, value);
    }

    public string SkinUrl
    {
        get => _skinUrl;
        set => SetField(ref _skinUrl, value);
    }

    /// <summary>皮肤位图（3D 预览纹理用）。为 null 时预览回退为占位。</summary>
    public BitmapImage? SkinImage
    {
        get => _skinImage;
        set => SetField(ref _skinImage, value);
    }

    /// <summary>是否为 slim（Alex）模型：左臂 3px 宽。</summary>
    public bool Slim => _modelType == "slim";

    public string ModelType
    {
        get => _modelType;
        set
        {
            if (SetField(ref _modelType, value))
                OnPropertyChanged(nameof(Slim));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public bool HasSkin
    {
        get => _hasSkin;
        set => SetField(ref _hasSkin, value);
    }

    public ICommand FetchSkinCommand { get; }

    public SkinViewModel()
    {
        FetchSkinCommand = new AsyncRelayCommand(_ => FetchSkinAsync());
    }

    private async Task FetchSkinAsync()
    {
        if (string.IsNullOrWhiteSpace(PlayerName)) return;
        IsBusy = true;
        HasSkin = false;
        SkinImage = null;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var skin = await SkinFetcher.FetchByUsernameAsync(client, PlayerName.Trim());
            if (skin is not null)
            {
                SkinUrl = skin.SkinUrl;
                ModelType = skin.Model;
                var bytes = await SkinFetcher.DownloadImageBytesAsync(client, skin.SkinUrl);
                if (bytes is { Length: > 0 })
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = new MemoryStream(bytes);
                    bmp.EndInit();
                    if (bmp.CanFreeze) bmp.Freeze();
                    SkinImage = bmp;
                }
                HasSkin = true;
                StatusMessage = $"已获取 {PlayerName} 的皮肤（{ModelType}）";
            }
            else
            {
                StatusMessage = $"未找到玩家 {PlayerName} 的皮肤";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"获取皮肤失败：{ex.Message}";
        }
        finally { IsBusy = false; }
    }
}
