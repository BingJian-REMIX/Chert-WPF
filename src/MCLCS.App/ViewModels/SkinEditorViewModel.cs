using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MCLCS.Core.Auth;
using MCLCS.Core.Mvvm;
using MCLCS.Core.Profiles;
using MCLCS.App.Services;

namespace MCLCS.App.ViewModels;

/// <summary>
/// 皮肤编辑器（规格 2.3 面板 13）：36 面子画布编辑 + 3D 实时预览 +
/// 取色板 + 对称绘制 + 撤销/重做 + PNG 导入导出 + 应用到离线账号。
/// </summary>
public class SkinEditorViewModel : ObservableObject
{
    private readonly byte[] _pixels = new byte[64 * 64 * 4];
    private WriteableBitmap _bitmap;
    private WriteableBitmap _faceBitmap;

    // 撤销栈
    private readonly Stack<byte[]> _undoStack = new();
    private readonly Stack<byte[]> _redoStack = new();
    private const int MaxUndo = 50;

    // 当前编辑状态
    private SkinPart? _selectedPart;
    private SkinFace? _selectedFace;
    private Color _primaryColor = Color.FromRgb(255, 255, 255);
    private Color _secondaryColor = Color.FromRgb(0, 0, 0);
    private int _brushSize = 1;
    private bool _symmetryEnabled = true;
    private bool _isEraser;
    private int _faceZoom = 10;
    private string _statusMessage = "";
    private bool _isLegacySkin;
    private bool _isSlim;

    // 调色板
    private Color? _colorPickerColor;

    public SkinEditorViewModel()
    {
        _bitmap = new WriteableBitmap(64, 64, 96, 96, PixelFormats.Bgra32, null);
        _faceBitmap = new WriteableBitmap(8, 8, 96, 96, PixelFormats.Bgra32, null);

        // 默认皮肤：优先用内嵌的 dummy.png（替代旧的全蓝底色），加载失败再回退到史蒂夫蓝。
        LoadDefaultSkin();
        FlushFull();
        SelectedPart = SkinLayout.Parts.FirstOrDefault();

        BrushCommand = new RelayCommand(p => Paint(p as Point?));
        FillCommand = new RelayCommand(p => FloodFill(p as Point?));
        UndoCommand = new RelayCommand(_ => Undo());
        RedoCommand = new RelayCommand(_ => Redo());
        ClearCommand = new RelayCommand(_ => { SaveUndo(); ClearToColor(Colors.Transparent); FlushFull(); });
        ExportCommand = new RelayCommand(_ => ExportSkin());
        ImportCommand = new RelayCommand(_ => ImportSkin());
        ApplyToAccountCommand = new RelayCommand(_ => ApplyToAccount());
    }

    // ---- 属性 ----

    public ObservableCollection<SkinPart> Parts => new(SkinLayout.Parts);

    public SkinPart? SelectedPart
    {
        get => _selectedPart;
        set { SetField(ref _selectedPart, value); if (value?.Faces.Count > 0) SelectedFace = value.Faces[0]; }
    }

    public SkinFace? SelectedFace
    {
        get => _selectedFace;
        set { SetField(ref _selectedFace, value); UpdateFacePreview(); }
    }

    public WriteableBitmap FullBitmap
    {
        get => _bitmap;
        set => SetField(ref _bitmap, value);
    }

    public WriteableBitmap FaceBitmap
    {
        get => _faceBitmap;
        set => SetField(ref _faceBitmap, value);
    }

    public Color PrimaryColor { get => _primaryColor; set => SetField(ref _primaryColor, value); }
    public Color SecondaryColor { get => _secondaryColor; set => SetField(ref _secondaryColor, value); }
    public int BrushSize { get => _brushSize; set => SetField(ref _brushSize, value); }
    public bool SymmetryEnabled { get => _symmetryEnabled; set => SetField(ref _symmetryEnabled, value); }
    public bool IsEraser { get => _isEraser; set => SetField(ref _isEraser, value); }
    public int FaceZoom { get => _faceZoom; set => SetField(ref _faceZoom, value); }
    /// <summary>是否旧版 64x32 皮肤（下半部分为空，3D 预览需将左肢回退到右肢纹理）。</summary>
    public bool IsLegacySkin { get => _isLegacySkin; set => SetField(ref _isLegacySkin, value); }

    /// <summary>3D 预览是否按纤细（Alex）手臂渲染（slim 模型）。</summary>
    public bool IsSlim { get => _isSlim; set => SetField(ref _isSlim, value); }
    public int FaceZoomedW => (SelectedFace?.W ?? 8) * FaceZoom;
    public int FaceZoomedH => (SelectedFace?.H ?? 8) * FaceZoom;
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public Color? ColorPickerColor
    {
        get => _colorPickerColor;
        set { _colorPickerColor = value; if (value.HasValue) PrimaryColor = value.Value; OnPropertyChanged(); }
    }

    public Color[] Palette { get; } =
    {
        Color.FromRgb(255,255,255), Color.FromRgb(180,180,180), Color.FromRgb(112,112,112), Color.FromRgb(56,56,56),
        Color.FromRgb(0,0,0), Color.FromRgb(240,120,120), Color.FromRgb(216,60,60), Color.FromRgb(164,96,60),
        Color.FromRgb(180,108,24), Color.FromRgb(240,180,48), Color.FromRgb(252,236,60), Color.FromRgb(120,204,48),
        Color.FromRgb(60,160,60), Color.FromRgb(48,124,168), Color.FromRgb(72,88,216), Color.FromRgb(136,64,176),
        Color.FromRgb(196,112,208), Color.FromRgb(160,100,80), Color.FromRgb(220,160,120), Color.FromRgb(240,200,168),
        Color.FromRgb(236,176,88), Color.FromRgb(224,128,0), Color.FromRgb(72,56,40), Color.FromRgb(240,124,140),
    };

    // ---- 命令 ----
    public ICommand BrushCommand { get; }
    public ICommand FillCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ApplyToAccountCommand { get; }

    // ---- 像素操作 ----

    public void Paint(Point? p)
    {
        if (p is null) return;
        SaveUndo();
        var px = (int)(p.Value.X / FaceZoom) + (SelectedFace?.SrcX ?? 0);
        var py = (int)(p.Value.Y / FaceZoom) + (SelectedFace?.SrcY ?? 0);
        var c = IsEraser ? Colors.Transparent : PrimaryColor;

        for (var dy = 0; dy < BrushSize; dy++)
        for (var dx = 0; dx < BrushSize; dx++)
        {
            SetPixel(px + dx, py + dy, c);
            if (SymmetryEnabled && SelectedFace is not null)
            {
                var (mx, my) = SkinLayout.MirrorPixel(px + dx, py + dy, SelectedFace);
                SetPixel(mx, my, c);
            }
        }
        FlushFull();
        UpdateFacePreview();
    }

    public void FloodFill(Point? p)
    {
        if (p is null || SelectedFace is null) return;
        SaveUndo();
        var px = (int)(p.Value.X / FaceZoom) + SelectedFace.SrcX;
        var py = (int)(p.Value.Y / FaceZoom) + SelectedFace.SrcY;
        var target = GetPixel(px, py);
        var c = IsEraser ? Colors.Transparent : PrimaryColor;
        if (ColorEquals(target, c)) return;

        FillRegion(SelectedFace.SrcX, SelectedFace.SrcY, SelectedFace.W, SelectedFace.H, px, py, target, c);
        if (SymmetryEnabled)
        {
            var mirror = SkinLayout.Mirror(SelectedFace);
            if (mirror is not null)
            {
                var (mx, my) = SkinLayout.MirrorPixel(px, py, SelectedFace);
                var mt = GetPixel(mx, my);
                FillRegion(mirror.SrcX, mirror.SrcY, mirror.W, mirror.H, mx, my, mt, c);
            }
        }
        FlushFull();
        UpdateFacePreview();
    }

    private void FillRegion(int rX, int rY, int rW, int rH, int sx, int sy, Color target, Color fill)
    {
        var stack = new Stack<(int, int)>();
        stack.Push((sx, sy));
        var visited = new HashSet<(int, int)>();
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < rX || cx >= rX + rW || cy < rY || cy >= rY + rH) continue;
            if (!visited.Add((cx, cy))) continue;
            if (!ColorEquals(GetPixel(cx, cy), target)) continue;
            SetPixel(cx, cy, fill);
            stack.Push((cx + 1, cy)); stack.Push((cx - 1, cy));
            stack.Push((cx, cy + 1)); stack.Push((cx, cy - 1));
        }
    }

    // ---- 撤销/重做 ----

    private void SaveUndo()
    {
        _undoStack.Push((byte[])_pixels.Clone());
        _redoStack.Clear();
        while (_undoStack.Count > MaxUndo) _undoStack.TryPop(out _);
    }

    private void Undo()
    {
        if (!_undoStack.TryPop(out var prev)) return;
        _redoStack.Push((byte[])_pixels.Clone());
        Array.Copy(prev, _pixels, _pixels.Length);
        FlushFull(); UpdateFacePreview();
    }

    private void Redo()
    {
        if (!_redoStack.TryPop(out var next)) return;
        _undoStack.Push((byte[])_pixels.Clone());
        Array.Copy(next, _pixels, _pixels.Length);
        FlushFull(); UpdateFacePreview();
    }

    // ---- 导入/导出 ----

    private void ExportSkin()
    {
        var path = UIService.SaveFile("PNG|*.png", "保存皮肤");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var encoder = new PngBitmapEncoder();
            // 克隆后再编码，避免 BitmapFrame.Create 冻结活的 WriteableBitmap，导致后续编辑失败。
            encoder.Frames.Add(BitmapFrame.Create(_bitmap.Clone()));
            using var fs = File.Create(path);
            encoder.Save(fs);
            StatusMessage = $"已导出 {Path.GetFileName(path)}";
            ToastService.Show("皮肤编辑器", "已导出", ToastKind.Success);
        }
        catch (Exception ex) { StatusMessage = $"导出失败: {ex.Message}"; }
    }

    private void ImportSkin()
    {
        var path = UIService.PickFile("PNG|*.png", "导入皮肤 PNG");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var decoder = new PngBitmapDecoder(
                new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth != 64 || (frame.PixelHeight != 64 && frame.PixelHeight != 32))
            {
                StatusMessage = "皮肤必须为 64x64（新版）或 64x32（旧版）像素";
                return;
            }
            SaveUndo();
            // 统一转换为 Bgra32，避免灰度/索引 PNG 导致像素对齐错误（表现为条纹/压缩错乱）
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var src = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(src, converted.PixelWidth * 4, 0);
            Array.Clear(_pixels);
            // 复制像素；旧版 64x32 只填上半部分
            var copyH = Math.Min(64, converted.PixelHeight);
            for (var y = 0; y < copyH; y++)
                Array.Copy(src, y * converted.PixelWidth * 4,
                           _pixels, y * 64 * 4,
                           converted.PixelWidth * 4);
            IsLegacySkin = frame.PixelHeight == 32;
            FlushFull(); UpdateFacePreview();
            StatusMessage = $"已导入 {Path.GetFileName(path)}";
        }
        catch (Exception ex) { StatusMessage = $"导入失败: {ex.Message}"; }
    }

    private void ApplyToAccount()
    {
        var account = AccountStore.GetLastUsed(LauncherService.Instance.GameRoot);
        if (account is null || account.AuthType != "offline")
        { StatusMessage = "只能应用到离线账号"; return; }
        try
        {
            var skinDir = Path.Combine(LauncherService.Instance.GameRoot, "skins");
            Directory.CreateDirectory(skinDir);
            var skinPath = Path.Combine(skinDir, $"{account.Username}_skin.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_bitmap.Clone()));
            using var fs = File.Create(skinPath);
            encoder.Save(fs);
            StatusMessage = $"皮肤已应用到 {account.DisplayName}";
            ToastService.Show("皮肤编辑器", "已应用", ToastKind.Success);
        }
        catch (Exception ex) { StatusMessage = $"应用失败: {ex.Message}"; }
    }

    // ---- 内部方法 ----

    public void UpdateFacePreview()
    {
        if (SelectedFace is null) return;
        var fw = SelectedFace.W;
        var fh = SelectedFace.H;
        // 重建 face bitmap（确保尺寸匹配）
        _faceBitmap = new WriteableBitmap(fw, fh, 96, 96, PixelFormats.Bgra32, null);
        var facePx = new byte[fw * fh * 4];
        for (var y = 0; y < fh; y++)
        for (var x = 0; x < fw; x++)
        {
            var si = ((SelectedFace.SrcY + y) * 64 + SelectedFace.SrcX + x) * 4;
            var di = (y * fw + x) * 4;
            Array.Copy(_pixels, si, facePx, di, 4);
        }
        _faceBitmap.WritePixels(new Int32Rect(0, 0, fw, fh), facePx, fw * 4, 0);
        OnPropertyChanged(nameof(FaceBitmap));
        OnPropertyChanged(nameof(FaceZoomedW));
        OnPropertyChanged(nameof(FaceZoomedH));
    }

    private void SetPixel(int x, int y, Color c)
    {
        if (x < 0 || x >= 64 || y < 0 || y >= 64) return;
        var i = (y * 64 + x) * 4;
        _pixels[i] = c.B; _pixels[i + 1] = c.G;
        _pixels[i + 2] = c.R; _pixels[i + 3] = c.A;
    }

    private Color GetPixel(int x, int y)
    {
        if (x < 0 || x >= 64 || y < 0 || y >= 64) return Colors.Transparent;
        var i = (y * 64 + x) * 4;
        return Color.FromArgb(_pixels[i + 3], _pixels[i + 2], _pixels[i + 1], _pixels[i]);
    }

    private static bool ColorEquals(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;

    private void ClearToColor(Color c)
    {
        for (var i = 0; i < _pixels.Length; i += 4)
        { _pixels[i] = c.B; _pixels[i + 1] = c.G; _pixels[i + 2] = c.R; _pixels[i + 3] = c.A; }
    }

    /// <summary>
    /// 载入默认皮肤：读取程序集内嵌的 Resources/Skins/dummy.png（64x64 或 64x32）。
    /// 任意失败（资源缺失/尺寸不符）则回退到史蒂夫蓝底色，保证编辑器始终有可见画布。
    /// </summary>
    private void LoadDefaultSkin()
    {
        const string uri = "pack://application:,,,/MCLCS.App;component/Resources/Skins/dummy.png";
        try
        {
            var res = Application.GetResourceStream(new Uri(uri, UriKind.Absolute));
            if (res is null) throw new InvalidOperationException("找不到内嵌默认皮肤资源");
            using var stream = res.Stream;
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth != 64 || (frame.PixelHeight != 64 && frame.PixelHeight != 32))
                throw new InvalidOperationException("默认皮肤必须是 64x64 或 64x32");
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var src = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(src, converted.PixelWidth * 4, 0);
            Array.Clear(_pixels);
            var copyH = Math.Min(64, converted.PixelHeight);
            for (var y = 0; y < copyH; y++)
                Array.Copy(src, y * converted.PixelWidth * 4, _pixels, y * 64 * 4, converted.PixelWidth * 4);
            IsLegacySkin = frame.PixelHeight == 32;
        }
        catch
        {
            ClearToColor(Color.FromRgb(0x00, 0x9C, 0xFF));
        }
    }

    public void FlushFull()
    {
        // 3D 管线 / ImageBrush / 编码器都可能冻结/缓存位图，导致 WritePixels 失败。
        // 若已冻结则换新实例，保证皮肤编辑器仍可原地更新。
        if (_bitmap.IsFrozen)
            _bitmap = new WriteableBitmap(64, 64, 96, 96, PixelFormats.Bgra32, null);
        _bitmap.WritePixels(new Int32Rect(0, 0, 64, 64), _pixels, 64 * 4, 0);
        OnPropertyChanged(nameof(FullBitmap));
    }
}
