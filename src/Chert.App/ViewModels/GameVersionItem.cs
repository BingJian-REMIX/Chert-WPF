namespace Chert.App.ViewModels;

/// <summary>
/// 下载中心 / 版本安装页的「游戏版本」下拉项包装。
/// <see cref="Id"/> 为传给 Modrinth 过滤与比较用的纯净版本号（空串表示「全部版本」）；
/// <see cref="Display"/> 为下拉中展示的文字，已安装版本带「已装」标注，且排在最前。
/// ComboBox 绑定 <c>DisplayMemberPath="Display"</c> + <c>SelectedValuePath="Id"</c> +
/// <c>SelectedValue="{Binding SelectedGameVersion}"</c>，使 <c>SelectedGameVersion</c> 仍为纯版本号字符串。
/// </summary>
public sealed class GameVersionItem
{
    public string Id { get; init; } = "";

    public bool IsInstalled { get; init; }

    public string Display => string.IsNullOrEmpty(Id)
        ? "全部版本"
        : (IsInstalled ? $"{Id} 〔已装〕" : Id);

    public override string ToString() => Display;
}
