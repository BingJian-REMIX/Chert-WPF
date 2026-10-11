using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chert.App.Themes;
using Chert.Core.Auth;
using Chert.Core.Localization;

namespace Chert.App.Views;

/// <summary>
/// 外置登录账号存在多个游戏角色但未设定默认角色时弹出的选择框。
/// 列出服务器返回的所有可选角色，用户点选其一后由调用方据所选角色落地会话
/// （accessToken / userProperties 已由上层从认证结果中取到，无需再次请求网络）。
/// </summary>
public partial class AuthProfilePickerDialog : Window
{
    private readonly IReadOnlyList<AuthlibProfile> _profiles;
    private AuthlibProfile? _selected;
    private Border? _selectedRow;

    /// <summary>用户选中的角色；未选择或取消时为 null。</summary>
    public AuthlibProfile? SelectedProfile => _selected;

    public AuthProfilePickerDialog(IReadOnlyList<AuthlibProfile> profiles)
    {
        InitializeComponent();
        _profiles = profiles;
        BuildRows();

        Loaded += (_, _) =>
        {
            if (Owner is Window o && o.IsLoaded)
            {
                Left = o.Left;
                Top = o.Top;
                Width = o.ActualWidth;
                Height = o.ActualHeight;
            }
            try { AnimationHelper.PlayModalEnter(Card); } catch { }
        };
    }

    private void BuildRows()
    {
        foreach (var p in _profiles)
        {
            var nameTb = new TextBlock
            {
                Text = p.Name,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("PrimaryForeground")
            };
            var uuidTb = new TextBlock
            {
                Text = LocaleManager.Tf("authlib.pick_uuid", p.Uuid),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)FindResource("SecondaryForeground")
            };

            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
            stack.Children.Add(nameTb);
            stack.Children.Add(uuidTb);

            var row = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = (Brush)FindResource("ControlBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Child = stack
            };
            row.MouseLeftButtonUp += (_, _) => SelectRow(row, p);
            ProfilePanel.Children.Add(row);
        }
    }

    private void SelectRow(Border row, AuthlibProfile p)
    {
        _selected = p;

        if (_selectedRow is not null)
            _selectedRow.Background = Brushes.Transparent;

        if (FindResource("AccentBrush") is SolidColorBrush sb)
            row.Background = new SolidColorBrush(Color.FromArgb(0x24, sb.Color.R, sb.Color.G, sb.Color.B));
        else
            row.Background = new SolidColorBrush(Color.FromArgb(0x24, 0x6C, 0x5C, 0xFF));

        _selectedRow = row;
        OkButton.IsEnabled = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
