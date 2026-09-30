using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chert.App.Views;
using Chert.Core.Input;
using Chert.Core.Profiles;
using Chert.Core.Utils;

namespace Chert.App.Views;

/// <summary>
/// 清单 #11：触屏按键布局编辑器。左侧画布直接拖动按键，右侧调整名称 / 按键 / 大小，
/// 可增删按键与调整面板尺寸，保存后写入 profile 并对已打开的触屏面板即时生效。
/// </summary>
public class TouchLayoutEditorWindow : Window
{
    /// <summary>常用按键候选（名称 + 虚拟键码）。</summary>
    private static readonly (string Name, int Vk)[] CommonKeys =
    {
        ("W", 0x57), ("A", 0x41), ("S", 0x53), ("D", 0x44),
        ("Space（跳跃）", 0x20), ("Shift（潜行）", 0x10), ("Ctrl（疾跑）", 0x11),
        ("E（物品栏）", 0x45), ("Q（丢弃）", 0x51), ("F（副手）", 0x46),
        ("1", 0x31), ("2", 0x32), ("3", 0x33), ("4", 0x34), ("5", 0x35),
        ("Esc", 0x1B), ("F3", 0x72), ("F5", 0x74)
    };

    private readonly TouchControlConfig _cfg;
    private readonly Canvas _canvas;
    private readonly ListBox _list;
    private readonly TextBox _labelBox;
    private readonly ComboBox _keyCombo;
    private readonly TextBox _vkBox;
    private readonly Slider _sizeSlider;
    private readonly ComboBox _kindCombo;
    private readonly Slider _opacitySlider;
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;

    public TouchLayoutEditorWindow()
    {
        _cfg = ProfileStore.Load(GameConstants.DefaultGameRoot).Touch?.Clone() ?? TouchControlConfig.CreateDefault();
        if (_cfg.Buttons.Count == 0) _cfg.Buttons = TouchControlConfig.CreateDefault().Buttons;

        Title = "触屏按键布局";
        Width = 760;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)TryFindResource("WindowBackground") ?? new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x30));
        Foreground = (Brush)TryFindResource("PrimaryForeground") ?? new SolidColorBrush(Colors.White);

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ===== 左：画布 =====
        var leftHead = new StackPanel();
        var canvasHost = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0x88, 0x88, 0x88)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = _cfg.PanelWidth,
            Height = _cfg.PanelHeight + TouchPanelBuilder.HandleHeight
        };
        _canvas = TouchPanelBuilder.Build(_cfg, true);
        var wrap = new Grid();
        wrap.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TouchPanelBuilder.HandleHeight) });
        wrap.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var handle = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0x10, 0x10, 0x10)),
            CornerRadius = new CornerRadius(8, 8, 0, 0)
        };
        handle.Child = new TextBlock
        {
            Text = "拖动圆形按键调整位置",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetRow(handle, 0);
        Grid.SetRow(_canvas, 1);
        wrap.Children.Add(handle);
        wrap.Children.Add(_canvas);
        canvasHost.Child = wrap;
        leftHead.Children.Add(canvasHost);
        Grid.SetColumn(leftHead, 0);
        root.Children.Add(leftHead);

        // ===== 右：属性 =====
        var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };

        right.Children.Add(Title("按键列表"));
        _list = new ListBox { Height = 150, SelectionChanged = OnSelectionChanged };
        _list.DisplayMemberPath = "Label";
        right.Children.Add(_list);

        _labelBox = new TextBox { Margin = new Thickness(0, 8, 0, 0) };
        _labelBox.TextChanged += (_, _) => ApplyToSelected();
        right.Children.Add(Labeled("显示名称", _labelBox));

        _keyCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var k in CommonKeys) _keyCombo.Items.Add($"{k.Name} (0x{k.Vk:X2})");
        _keyCombo.SelectionChanged += (_, _) =>
        {
            if (_keyCombo.SelectedIndex >= 0)
            {
                _vkBox.Text = "0x" + CommonKeys[_keyCombo.SelectedIndex].Vk.ToString("X2");
                ApplyToSelected();
            }
        };
        right.Children.Add(Labeled("预设按键", _keyCombo));

        _vkBox = new TextBox { Margin = new Thickness(0, 4, 0, 0) };
        _vkBox.TextChanged += (_, _) => ApplyToSelected();
        right.Children.Add(Labeled("虚拟键码", _vkBox));

        _kindCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        _kindCombo.Items.Add("方向键（按住持续）");
        _kindCombo.Items.Add("动作键（按下触发）");
        _kindCombo.SelectedIndex = 1;
        _kindCombo.SelectionChanged += (_, _) => ApplyToSelected();
        right.Children.Add(Labeled("类型", _kindCombo));

        _sizeSlider = new Slider { Minimum = 36, Maximum = 120, TickFrequency = 2, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 0) };
        _sizeSlider.ValueChanged += (_, _) => ApplyToSelected();
        right.Children.Add(Labeled("按键大小", _sizeSlider));

        var addBtn = new Button { Content = "添加按键", Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(10, 4) };
        addBtn.Click += (_, _) => AddButton();
        var delBtn = new Button { Content = "删除选中", Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(10, 4) };
        delBtn.Click += (_, _) => RemoveSelected();
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        btnRow.Children.Add(addBtn);
        btnRow.Children.Add(delBtn);
        right.Children.Add(btnRow);

        right.Children.Add(Title("面板"));
        _widthBox = new TextBox { Text = _cfg.PanelWidth.ToString(), Margin = new Thickness(0, 4, 0, 0) };
        _heightBox = new TextBox { Text = _cfg.PanelHeight.ToString(), Margin = new Thickness(0, 4, 0, 0) };
        right.Children.Add(Labeled("宽度", _widthBox));
        right.Children.Add(Labeled("高度", _heightBox));

        _opacitySlider = new Slider { Minimum = 0.2, Maximum = 1, TickFrequency = 0.05, IsSnapToTickEnabled = true, Value = _cfg.Opacity, Margin = new Thickness(0, 4, 0, 0) };
        right.Children.Add(Labeled("不透明度", _opacitySlider));

        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        // ===== 底部按钮 =====
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var resetBtn = new Button { Content = "恢复默认", Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 8, 0) };
        resetBtn.Click += (_, _) => ResetDefault();
        var cancelBtn = new Button { Content = "取消", Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Click += (_, _) => Close();
        var saveBtn = new Button { Content = "保存", Padding = new Thickness(16, 4) };
        saveBtn.Click += (_, _) => Save();
        bottom.Children.Add(resetBtn);
        bottom.Children.Add(cancelBtn);
        bottom.Children.Add(saveBtn);
        Grid.SetRow(bottom, 1);
        Grid.SetColumnSpan(bottom, 2);
        root.Children.Add(bottom);

        Content = root;
        RefreshList();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private static TextBlock Title(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 10, 0, 4),
        Foreground = (Brush)Application.Current.TryFindResource("PrimaryForeground") ?? Brushes.White
    };

    private static StackPanel Labeled(string text, Control c)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        sp.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = (Brush)Application.Current.TryFindResource("SecondaryForeground") ?? Brushes.LightGray
        });
        sp.Children.Add(c);
        return sp;
    }

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var b in _cfg.Buttons) _list.Items.Add(b);
    }

    private TouchButtonConfig? Selected => _list.SelectedItem as TouchButtonConfig;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var b = Selected;
        if (b is null) return;
        _suppress = true;
        _labelBox.Text = b.Label;
        _vkBox.Text = "0x" + b.Vk.ToString("X2");
        _kindCombo.SelectedIndex = b.Kind == TouchButtonKind.Direction ? 0 : 1;
        _sizeSlider.Value = b.Size;
        _suppress = false;
    }

    private bool _suppress;

    private void ApplyToSelected()
    {
        if (_suppress) return;
        var b = Selected;
        if (b is null) return;
        b.Label = _labelBox.Text;
        b.KeyName = ExtractKeyName(b.KeyName, _keyCombo.SelectedIndex, _vkBox.Text);
        if (int.TryParse(_vkBox.Text.Replace("0x", "").Trim(), System.Globalization.NumberStyles.HexNumber, null, out var vk)) b.Vk = vk;
        b.Kind = _kindCombo.SelectedIndex == 0 ? TouchButtonKind.Direction : TouchButtonKind.Action;
        b.Size = _sizeSlider.Value;
        RebuildCanvas();
    }

    private static string ExtractKeyName(string current, int presetIndex, string vkText)
    {
        if (presetIndex >= 0 && presetIndex < CommonKeys.Length)
            return CommonKeys[presetIndex].Name.Split('（')[0];
        return string.IsNullOrWhiteSpace(current) ? vkText : current;
    }

    private void RebuildCanvas()
    {
        var fresh = TouchPanelBuilder.Build(_cfg, true);
        // Canvas 的子元素不能同时属于两个父级：先全部摘出，再挂到当前画布
        var items = fresh.Children.Cast<UIElement>().ToList();
        foreach (var el in items) fresh.Children.Remove(el);
        _canvas.Children.Clear();
        foreach (var el in items) _canvas.Children.Add(el);
        _canvas.Width = _cfg.PanelWidth;
        _canvas.Height = _cfg.PanelHeight;
    }

    private void AddButton()
    {
        var idx = _keyCombo.SelectedIndex;
        var name = idx >= 0 ? CommonKeys[idx].Name.Split('（')[0] : "按键";
        var vk = idx >= 0 ? CommonKeys[idx].Vk : 0x20;
        var b = new TouchButtonConfig
        {
            Label = name,
            KeyName = name,
            Vk = vk,
            Kind = TouchButtonKind.Action,
            Size = 58,
            X = 20,
            Y = 20
        };
        _cfg.Buttons.Add(b);
        RefreshList();
        _list.SelectedItem = b;
        RebuildCanvas();
    }

    private void RemoveSelected()
    {
        if (Selected is not { } b) return;
        _cfg.Buttons.Remove(b);
        RefreshList();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        RebuildCanvas();
    }

    private void ResetDefault()
    {
        var d = TouchControlConfig.CreateDefault();
        _cfg.Buttons = d.Buttons;
        _cfg.PanelWidth = d.PanelWidth;
        _cfg.PanelHeight = d.PanelHeight;
        _cfg.Opacity = d.Opacity;
        _widthBox.Text = d.PanelWidth.ToString();
        _heightBox.Text = d.PanelHeight.ToString();
        _opacitySlider.Value = d.Opacity;
        RefreshList();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        RebuildCanvas();
    }

    private void Save()
    {
        try
        {
            if (int.TryParse(_widthBox.Text, out var w)) _cfg.PanelWidth = Math.Clamp(w, 180, 1200);
            if (int.TryParse(_heightBox.Text, out var h)) _cfg.PanelHeight = Math.Clamp(h, 120, 800);
            _cfg.Opacity = _opacitySlider.Value;
            _cfg.Enabled = true;
            // 坐标可能超出新面板尺寸，统一收敛一次
            foreach (var b in _cfg.Buttons)
            {
                b.X = Math.Clamp(b.X, 0, Math.Max(0, _cfg.PanelWidth - b.Size));
                b.Y = Math.Clamp(b.Y, 0, Math.Max(0, _cfg.PanelHeight - b.Size));
            }
            TouchOverlayWindow.ApplyConfig(_cfg);
        }
        catch { /* 非关键 */ }
        Close();
    }
}
