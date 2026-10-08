using System.Text.Json;

namespace Chert.Core.Localization;

public static class LocaleManager
{
    private static readonly Dictionary<string, Dictionary<string, string>> _locales = new(StringComparer.OrdinalIgnoreCase);
    private static string _currentLocale = "zh_CN";

    public static event Action<string>? LocaleChanged;

    public static string CurrentLocale
    {
        get => _currentLocale;
        set
        {
            if (_locales.ContainsKey(value) && !string.Equals(_currentLocale, value, StringComparison.OrdinalIgnoreCase))
            {
                _currentLocale = value;
                LocaleChanged?.Invoke(value);
            }
        }
    }

    public static List<string> AvailableLocales => _locales.Keys.ToList();

    public static void LoadLocale(string localeCode, string jsonContent)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);
        if (dict is not null)
            _locales[localeCode] = dict;
    }

    public static string T(string key)
    {
        if (_locales.TryGetValue(_currentLocale, out var dict) && dict.TryGetValue(key, out var value))
            return value;
        if (_currentLocale != "en_US" && _locales.TryGetValue("en_US", out var enDict) && enDict.TryGetValue(key, out var enValue))
            return enValue;
        if (_locales.TryGetValue("zh_CN", out var zhDict) && zhDict.TryGetValue(key, out var zhValue))
            return zhValue;
        return key;
    }

    public static string Tf(string key, params object[] args)
    {
        try { return string.Format(T(key), args); }
        catch { return T(key); }
    }

    public static string NormalizeLocaleCode(string? code) => code switch
    {
        "zh-CN" or "zh_CN" or "zh-Hans" or "zh-Hant-CN" => "zh_CN",
        "en-US" or "en_US" or "en" => "en_US",
        "zh-TW" or "zh_TW" or "zh-Hant" or "zh-Hant-TW" or "zh-HK" => "zh_TW",
        _ when _locales.ContainsKey(code ?? "") => code!,
        _ => "zh_CN"
    };

    static LocaleManager()
    {
        LoadLocale("zh_CN", BuiltInZhCN());
        LoadLocale("en_US", BuiltInEnUS());
        LoadLocale("zh_TW", BuiltInZhTW());
    }

                        private static string BuiltInZhCN() => @"{
  ""app.title"": ""燧石启动器"",
  ""app.launcher"": ""燧石启动器"",
  ""tab.launch"": ""启动游戏"",
  ""tab.install"": ""安装版本"",
  ""tab.download"": ""下载中心"",
  ""tab.settings"": ""设置"",
  ""tab.crash"": ""崩溃分析"",
  ""tab.mods"": ""Mod 管理"",
  ""tab.accounts"": ""账号管理"",
  ""tab.skin"": ""皮肤预览"",
  ""btn.launch"": ""启动游戏"",
  ""btn.install"": ""安装"",
  ""btn.cancel"": ""取消"",
  ""btn.save"": ""保存"",
  ""btn.refresh"": ""刷新"",
  ""btn.search"": ""搜索"",
  ""btn.download"": ""下载"",
  ""btn.login"": ""登录"",
  ""btn.logout"": ""登出"",
  ""btn.add_account"": ""添加账号"",
  ""btn.delete"": ""删除"",
  ""btn.check_updates"": ""检查更新"",
  ""btn.check_deps"": ""依赖检查"",
  ""lbl.version"": ""版本"",
  ""lbl.memory"": ""内存"",
  ""lbl.username"": ""用户名"",
  ""lbl.java_path"": ""Java 路径"",
  ""lbl.game_dir"": ""游戏目录"",
  ""lbl.install_type"": ""安装类型"",
  ""lbl.vanilla"": ""原版"",
  ""lbl.fabric"": ""Fabric"",
  ""lbl.forge"": ""Forge"",
  ""lbl.modpack"": ""整合包"",
  ""lbl.modrinth_pack"": ""Modrinth 整合包"",
  ""lbl.account_type"": ""账号类型"",
  ""lbl.offline"": ""离线"",
  ""lbl.microsoft"": ""微软"",
  ""lbl.authlib"": ""Authlib-Injector"",
  ""lbl.theme"": ""主题"",
  ""lbl.language"": ""语言"",
  ""lbl.light"": ""亮色"",
  ""lbl.dark"": ""暗色"",
  ""lbl.chinese"": ""简体中文"",
  ""lbl.english"": ""English"",
  ""lbl.no_mods"": ""未找到已安装的 Mod"",
  ""lbl.no_deps_issues"": ""未检测到依赖问题"",
  ""lbl.deps_ok"": ""所有依赖已满足"",
  ""lbl.missing_deps"": ""缺失依赖"",
  ""lbl.conflict_deps"": ""冲突 Mod"",
  ""lbl.required"": ""必需"",
  ""lbl.optional"": ""可选"",
  ""lbl.search_mods"": ""搜索 Mod、光影、材质包..."",
  ""lbl.crash_analysis"": ""崩溃分析"",
  ""lbl.no_crash"": ""未检测到崩溃报告"",
  ""msg.installing"": ""正在安装 {0}..."",
  ""msg.install_done"": ""{0} 安装完成"",
  ""msg.install_failed"": ""{0} 安装失败"",
  ""msg.downloading"": ""正在下载 ({0}/{1})..."",
  ""msg.launching"": ""正在启动 {0}..."",
  ""msg.crashed"": ""游戏崩溃：{0}"",
  ""msg.normal_exit"": ""游戏正常退出"",
  ""msg.dep_missing"": ""缺少依赖：{0} ({1})"",
  ""msg.dep_conflict"": ""冲突：{0}（已安装 {1}，冲突范围 {2}）"",
  ""msg.skin_fetch_failed"": ""获取皮肤失败"",
  ""msg.ms_login_hint"": ""请在浏览器中打开 {0} 并输入代码 {1}"",
  ""msg.authlib_login_failed"": ""Authlib-Injector 登录失败"",
  ""crash.policy"": ""崩溃自动修复"",
  ""crash.policy.always"": ""始终开启"",
  ""crash.policy.ask"": ""每次询问"",
  ""crash.policy.never"": ""始终拒绝"",
  ""crash.repairable"": ""检测到可自动修复的问题"",
  ""crash.not_repairable"": ""无法自动修复（需手动处理）"",
  ""crash.btn_repair"": ""尝试自动修复"",
  ""crash.repairing"": ""正在尝试自动修复…"",
  ""crash.repaired_success"": ""已修复并成功启动游戏！"",
  ""crash.repaired_recrash"": ""已尝试修复，但游戏再次崩溃，可继续尝试。"",
  ""crash.repair_unrepairable"": ""已尝试修复但仍崩溃，且无法继续自动修复。"",
  ""crash.repair_failed"": ""自动修复失败：{0}"",
  ""crash.non_destructive"": ""所有修复操作均不会删除或修改游戏原文件。"",
  ""crash.analyzing"": ""正在分析崩溃报告…"",
  ""crash.open_report"": ""打开崩溃分析报告"",
  ""tab.game"": ""游戏"",
  ""tab.toolbox"": ""工具箱"",
  ""tab.minecraft"": ""Minecraft"",
  ""tab.shader"": ""光影"",
  ""tab.resourcepack"": ""材质包"",
  ""tab.map"": ""地图"",
  ""tab.install"": ""安装"",
  ""status.java"": ""Java:"",
  ""status.installed"": ""已安装 {0} 个版本"",
  ""status.running"": ""运行 {0} 个实例"",
  ""status.no_java"": ""未检测到 Java"",
  ""status.java_low"": ""Java {0}（低于所需 {1}）"",
  ""status.network_ok"": ""网络正常"",
  ""status.network_slow"": ""网络延迟"",
  ""status.network_offline"": ""离线"",
  ""game.quick_launch"": ""快速启动"",
  ""game.lan"": ""局域网游戏"",
  ""game.servers"": ""服务器列表"",
  ""game.recommend"": ""智能推荐"",
  ""game.stats"": ""统计"",
  ""game.no_lan"": ""未发现局域网游戏，点击刷新扫描"",
  ""game.no_servers"": ""暂无服务器，点击添加"",
  ""game.add_server"": ""添加服务器"",
  ""game.join"": ""加入"",
  ""game.edit"": ""编辑"",
  ""game.password_protected"": ""需要密码"",
  ""game.players"": ""{0} 人"",
  ""game.latency"": ""延迟"",
  ""game.ping_good"": ""良好"",
  ""game.ping_ok"": ""一般"",
  ""game.ping_bad"": ""较差"",
  ""game.install_mod"": ""一键安装"",
  ""game.not_interested"": ""不感兴趣"",
  ""game.dep_missing_red"": ""缺失依赖（红色标记）"",
  ""game.recent_version"": ""最近版本"",
  ""game.weekly_time"": ""本周时长"",
  ""game.crash_count"": ""崩溃次数"",
  ""game.annual_report"": ""年度报告"",
  ""game.mode_survival"": ""生存"",
  ""game.mode_creative"": ""创造"",
  ""game.mode_adventure"": ""冒险"",
  ""game.mode_spectator"": ""旁观"",
  ""game.btn_start"": ""启动"",
  ""game.account"": ""账号"",
  ""game.no_account"": ""（无账号，使用离线昵称）"",
  ""game.username"": ""用户名"",
  ""game.memory"": ""内存 MB"",
  ""dl.search_hint"": ""搜索 Mod、光影、材质包..."",
  ""dl.filter_version"": ""版本过滤"",
  ""dl.filter_loader"": ""加载器过滤"",
  ""dl.queue"": ""下载队列"",
  ""dl.pause"": ""暂停"",
  ""dl.retry"": ""重试"",
  ""dl.pause_all"": ""全部暂停"",
  ""dl.resume_all"": ""继续队列"",
  ""dl.speed_limit"": ""限速"",
  ""dl.speed_limit_tip"": ""0 表示不限速；限速对全部下载生效"",
  ""dl.auto_retry"": ""失败自动重试"",
  ""dl.resume"": ""继续"",
  ""dl.clear_queue"": ""清空队列"",
  ""dl.no_results"": ""未找到结果"",
  ""dl.install_modpack"": ""安装整合包"",
  ""dl.browse_modrinth"": ""浏览 Modrinth"",
  ""dl.category"": ""分类"",
  ""dl.sort"": ""排序"",
  ""dl.sort_hot"": ""热门"",
  ""dl.sort_new"": ""最新"",
  ""dl.sort_downloads"": ""下载量"",
  ""dl.map_views"": ""{0} 次浏览"",
  ""dl.map_author"": ""作者: {0}"",
  ""dl.detail"": ""详情"",
  ""dl.extra_resources"": ""附加资源"",
  ""dl.downloading"": ""下载中"",
  ""dl.completed"": ""已完成"",
  ""tool.log"": ""日志管理器"",
  ""tool.crash"": ""崩溃分析"",
  ""tool.perf"": ""性能监控"",
  ""tool.network"": ""网络诊断"",
  ""tool.filewatch"": ""文件变更检测"",
  ""tool.datapack"": ""数据包冲突检测"",
  ""tool.saves"": ""存档管理器"",
  ""tool.backup"": ""备份管理器"",
  ""tool.screenshot"": ""截图管理器"",
  ""tool.clean"": ""清理冗余文件"",
  ""tool.modpackio"": ""整合包导入导出"",
  ""tool.music"": ""音乐播放器"",
  ""tool.moddev"": ""Mod 开发环境"",
  ""tool.packmaker"": ""资源包创建器"",
  ""tool.nbt"": ""NBT 编辑器"",
  ""tool.command"": ""命令语法表"",
  ""tool.map"": ""地图安装"",
  ""tool.skin"": ""皮肤编辑器"",
  ""tool.seasonal"": ""节日中心"",
  ""tool.shortcut"": ""快捷方式生成器"",
  ""tool.afk"": ""挂机工作流"",
  ""tool.aichat"": ""AI 助手"",
  ""tool.group.diag"": ""诊断与排障"",
  ""tool.group.resource"": ""资源与内容"",
  ""tool.group.dev"": ""开发工具"",
  ""tool.group.other"": ""其他"",
  ""settings.general"": ""通用"",
  ""settings.launch"": ""启动"",
  ""settings.recommend"": ""推荐"",
  ""settings.account"": ""账号"",
  ""settings.music"": ""音乐"",
  ""music.set_auto_duck"": ""游戏启动时自动降低音乐音量"",
  ""music.set_resume"": ""启动器启动时自动续播上次音乐"",
  ""music.set_client_section"": ""本地客户端模式"",
  ""music.set_client_section_tip"": ""由外部音乐客户端播放，启动器只负责在切走音源且客户端持续暂停后回收它。关闭总开关后完全不涉及外部客户端进程。"",
  ""music.set_client_enabled"": ""启用本地客户端模式"",
  ""music.set_client_grace"": ""客户端宽限期"",
  ""music.set_client_grace_tip"": ""播放器切到本地文件夹或 API 模式，且所有由启动器拉起的客户端都处于暂停状态时，持续此时间后直接结束这些客户端进程。设为 0 表示不自动关闭。"",
  ""music.set_lyric_pin"": ""歌词固定方式"",
  ""music.set_lyric_pin_tip"": ""只影响歌词叠加层的渲染行数。"",
  ""music.set_lyric_pin_current"": ""仅当前行"",
  ""music.set_lyric_pin_next"": ""当前行 + 下一行"",
  ""music.set_lyric_pin_prev"": ""上一行 + 当前行 + 下一行"",
  ""music.set_client_lyric"": ""客户端模式下仍用 API 获取歌词"",
  ""music.set_client_lyric_tip"": ""开启后，本地客户端模式下叠加层仍显示歌词（按歌名从在线歌词库匹配）；关闭则隐藏。"",
  ""music.set_client_app"": ""客户端程序"",
  ""music.set_client_tip"": ""提示：本地客户端模式下启动器不输出音频，切回本地文件夹或 API 模式后，宽限期到期会自动结束由启动器拉起的客户端。"",
  ""music.source_api"": ""在线"",
  ""music.api_search"": ""搜索"",
  ""music.api_keyword_tip"": ""输入歌曲名或「歌名 歌手」"",
  ""music.api_playlist_id"": ""歌单 ID"",
  ""music.api_playlist_id_tip"": ""在音乐客户端里分享歌单，链接末尾的数字就是 ID"",
  ""music.api_import"": ""导入"",
  ""music.api_my_playlists"": ""我的歌单"",
  ""music.api_refresh"": ""刷新"",
  ""music.api_login"": ""扫码登录"",
  ""music.api_logout"": ""退出登录"",
  ""music.api_cancel"": ""取消"",
  ""music.api_account"": ""账户"",
  ""music.api_scan_tip"": ""用手机 App 扫描二维码并确认登录"",
  ""music.api_results"": ""搜索结果"",
  ""music.api_playlists_section"": ""歌单"",
  ""music.set_api_section"": ""在线音源"",
  ""music.set_api_section_tip"": ""由启动器自己通过网络接口取流播放。默认指到 Meting 公共实例；若要用「我的歌单」，请改用网易云 API 并填写自建服务地址。"",
  ""music.set_api_enabled"": ""启用在线音源"",
  ""music.set_api_kind"": ""数据源"",
  ""music.set_api_kind_meting"": ""Meting 聚合（免登录）"",
  ""music.set_api_kind_netease"": ""网易云 API（支持扫码登录）"",
  ""music.set_api_kind_vendor"": ""厂家自建 API（扫码登录）"",
  ""music.set_api_baseurl"": ""服务地址"",
  ""music.set_api_baseurl_tip"": ""不含结尾斜杠。切换数据源时会重置为对应的默认地址。"",
  ""music.set_api_default"": ""恢复默认"",
  ""music.set_api_platform"": ""平台"",
  ""music.set_api_platform_netease"": ""网易云音乐"",
  ""music.set_api_platform_tencent"": ""QQ 音乐"",
  ""music.set_api_platform_kugou"": ""酷狗音乐"",
  ""music.set_api_platform_kuwo"": ""酷我音乐"",
  ""music.set_api_platform_baidu"": ""百度音乐"",
  ""music.set_api_platform_xiami"": ""虾米音乐"",
  ""music.set_api_platform_all"": ""全部平台（聚合搜索）"",
  ""music.set_api_quality"": ""音质"",
  ""music.set_api_quality_standard"": ""标准 128k"",
  ""music.set_api_quality_higher"": ""较高 192k"",
  ""music.set_api_quality_exquisite"": ""极高 320k"",
  ""music.set_api_quality_lossless"": ""无损"",
  ""music.set_api_account"": ""登录状态"",
  ""music.set_api_login_unsupported"": ""当前数据源不支持扫码登录"",
  ""settings.ai"": ""AI"",
  ""settings.appearance"": ""外观"",
  ""settings.about"": ""关于"",
  ""settings.lang"": ""语言"",
  ""settings.download"": ""下载"",
  ""settings.autostart"": ""开机自启"",
  ""settings.minimize_tray"": ""最小化到托盘"",
  ""settings.animations"": ""动画效果"",
  ""settings.toast_duration"": ""通知停留时长（秒）"",
  ""settings.filewatch"": ""文件变更检测"",
  ""settings.default_memory"": ""默认内存 (MB)"",
  ""settings.default_username"": ""默认用户名"",
  ""settings.java_path"": ""Java 路径"",
  ""settings.extra_jvm"": ""额外 JVM 参数"",
  ""settings.repair_policy"": ""修复策略"",
  ""settings.repair_always"": ""始终自动修复"",
  ""settings.repair_ask"": ""每次询问"",
  ""settings.repair_never"": ""拒绝修复"",
  ""settings.java_vendor"": ""Java 厂商"",
  ""settings.java_auto"": ""自动选择"",
  ""settings.prewarm"": ""启动预热"",
  ""settings.hud"": ""HUD 叠加"",
  ""settings.launch_compat"": ""启动前兼容性检测"",
  ""settings.dl_mirror"": ""镜像策略"",
  ""settings.dl_mirror_auto"": ""自动"",
  ""settings.dl_mirror_bmcl"": ""BMCLAPI 优先"",
  ""settings.dl_mirror_official"": ""仅官方"",
  ""settings.dl_concurrent"": ""最大并发下载数"",
  ""settings.auto_deps"": ""自动安装缺失前置"",
  ""settings.auto_deps_always"": ""始终"",
  ""settings.auto_deps_ask"": ""询问"",
  ""settings.auto_deps_off"": ""关闭"",
  ""settings.repair_rp"": ""资源包自动修复"",
  ""settings.server_pack_cache"": ""服务器资源包缓存"",
  ""settings.recommend_mode"": ""推荐模式"",
  ""settings.recommend_on"": ""启用（本地+在线）"",
  ""settings.recommend_local"": ""仅本地规则"",
  ""settings.recommend_off"": ""禁用"",
  ""settings.gameplay_prefs"": ""玩法偏好"",
  ""settings.add_offline"": ""添加离线账号"",
  ""settings.offline_name"": ""玩家名"",
  ""settings.login_ms"": ""Microsoft 登录"",
  ""settings.add_authlib"": ""添加 Authlib 账号"",
  ""settings.authlib_url"": ""服务器地址"",
  ""settings.authlib_email"": ""邮箱"",
  ""settings.authlib_password"": ""密码"",
  ""settings.ai_enable"": ""启用 AI 助手"",
  ""settings.ai_mode"": ""部署方式"",
  ""settings.ai_external"": ""外部 API"",
  ""settings.ai_local"": ""本地 Ollama"",
  ""settings.ai_endpoint"": ""API 地址"",
  ""settings.ai_key"": ""API Key"",
  ""settings.ai_model"": ""模型"",
  ""settings.ai_test"": ""测试连接"",
  ""settings.ai_status_ok"": ""服务正常"",
  ""settings.ai_status_warn"": ""响应缓慢"",
  ""settings.ai_status_err"": ""无法连接"",
  ""settings.ai_crash"": ""崩溃解读"",
  ""settings.ai_recommend"": ""推荐理由"",
  ""settings.ai_translate"": ""Mod 翻译"",
  ""settings.theme_color"": ""主题色"",
  ""settings.pick_color"": ""取色…"",
  ""settings.background"": ""背景图"",
  ""settings.font_scale"": ""字体大小"",
  ""settings.tab_colors"": ""索引贴颜色"",
  ""settings.tab_game"": ""游戏标签"",
  ""settings.tab_download"": ""下载标签"",
  ""settings.tab_toolbox"": ""工具箱标签"",
  ""settings.tab_settings"": ""设置标签"",
  ""settings.high_dpi"": ""适配高分辨率屏幕（启用 2x 图标）"",
  ""about.version"": ""版本"",
  ""about.check_update"": ""检查更新"",
  ""about.license"": ""开源许可"",
  ""about.github"": ""GitHub 仓库"",
  ""btn.ok"": ""确定"",
  ""btn.yes"": ""是"",
  ""btn.no"": ""否"",
  ""btn.close"": ""关闭"",
  ""btn.add"": ""添加"",
  ""btn.edit"": ""编辑"",
  ""btn.remove"": ""移除"",
  ""btn.back"": ""返回"",
  ""btn.next"": ""下一步"",
  ""btn.finish"": ""完成"",
  ""btn.export"": ""导出"",
  ""btn.import"": ""导入"",
  ""btn.apply"": ""应用"",
  ""btn.reset"": ""重置"",
  ""btn.copy"": ""复制"",
  ""btn.paste"": ""粘贴"",
  ""btn.browse"": ""浏览..."",
  ""btn.clear"": ""清除"",
  ""color.lightness"": ""亮度"",
  ""color.palette"": ""完整色板"",
  ""color.plane"": ""色域（横：色相 · 纵：饱和度）"",
  ""msg.loading"": ""加载中..."",
  ""msg.no_data"": ""暂无数据"",
  ""msg.success"": ""操作成功"",
  ""msg.failed"": ""操作失败"",
  ""msg.confirm_delete"": ""确认删除？此操作不可撤销。"",
  ""msg.saving"": ""保存中..."",
  ""msg.saved"": ""已保存"",
  ""msg.connected"": ""已连接"",
  ""msg.disconnected"": ""未连接"",
  ""msg.connecting"": ""连接中..."",
  ""status.ready"": ""就绪"",
  ""home.game.desc"": ""启动器主页（版本选择 / 启动游戏 / 实例管理）。"",
  ""theme.editor.title"": ""四色主题自定义（Core.TabThemeConfig）"",
  ""theme.editor.hint"": ""修改后立即反映到标题栏与侧栏选中色；非法值（非 #RRGGBB）会被 Core 自动回退默认色。"",
  ""java.title"": ""Java 环境检测"",
  ""java.detect"": ""检测本机 Java"",
  ""java.scanning"": ""正在扫描 Java（JAVA_HOME / /usr/lib/jvm / /opt/java / PATH）..."",
  ""java.detected"": ""检测到 {0} 个 Java 安装"",
  ""tool.log.desc"": ""查看 / 搜索 / 过滤 / 导出游戏日志与崩溃报告。"",
  ""tool.clean.desc"": ""扫描并清理游戏目录中的冗余与残留文件。"",
  ""tool.backup.desc"": ""管理存档与配置的手动 / 自动备份。"",
  ""tool.screenshot.desc"": ""浏览、删除与打包游戏截图。"",
  ""tool.crash.desc"": ""分析崩溃报告，定位异常类型与修复建议。"",
  ""tool.datapack.desc"": ""扫描数据包冲突与格式问题，给出处理建议。"",
  ""tool.saves.desc"": ""管理存档：兼容性与损坏检测、降级与备份。"",
  ""tool.skin.desc"": ""预览与编辑玩家皮肤，校验尺寸与模型。"",
  ""tab.minecraft.desc"": ""下载与安装 Minecraft 游戏版本。"",
  ""tab.mods.desc"": ""浏览、安装与管理 Mod，处理依赖与冲突。"",
  ""tab.shader.desc"": ""光影包的安装、预览与配置。"",
  ""tab.resourcepack.desc"": ""材质包的安装、预览与管理。"",
  ""tab.modpack.desc"": ""整合包的导入、安装与升级。"",
  ""tab.map.desc"": ""地图存档的下载与导入。"",
  ""tool.perf.desc"": ""实时性能监控与 FPS / TPS 分析。"",
  ""tool.network.desc"": ""网络连接与登录服务器诊断。"",
  ""tool.filewatch.desc"": ""监控游戏目录文件变更。"",
  ""tool.modpackio.desc"": ""整合包的导入与导出。"",
  ""tool.music.desc"": ""背景音乐与音轨管理。"",
  ""tool.moddev.desc"": ""模组开发辅助（模板 / 调试）。"",
  ""tool.packmaker.desc"": ""整合包制作与打包。"",
  ""tool.nbt.desc"": ""NBT 数据结构编辑器。"",
  ""tool.command.desc"": ""生成复杂命令与函数。"",
  ""tool.shortcut.desc"": ""生成桌面 / 开始菜单快捷方式。"",
  ""tool.afk.desc"": ""挂机与轻量自动化。"",
  ""tool.aichat.desc"": ""对接 Core.Ai 的聊天助手。"",
  ""settings.general.desc"": ""启动器的常规行为与外观设置。"",
  ""settings.launch.desc"": ""游戏启动参数、Java 与内存设置。"",
  ""settings.download.desc"": ""下载源、并发与缓存设置。"",
  ""settings.recommend.desc"": ""推荐内容与个性化设置。"",
  ""settings.account.desc"": ""账号登录与多账号管理。"",
  ""settings.ai.desc"": ""AI 助手相关配置。"",
  ""settings.appearance.desc"": ""主题与四色配色自定义。"",
  ""settings.about.desc"": ""关于燧石启动器与版本信息。"",
  ""tool.versionlist"": ""版本列表"",
  ""tool.versionlist.desc"": ""管理已安装的游戏版本，选择并一键启动。"",
  ""tool.devtools"": ""开发工具"",
  ""tool.devtools.desc"": ""Mod 骨架生成、资源包创建器与命令速查表。"",
  ""version.settings.title"": ""版本设置"",
  ""version.display_name"": ""显示名"",
  ""version.isolation"": ""隔离模式"",
  ""isolation.shared"": ""共享"",
  ""isolation.auto"": ""自动隔离"",
  ""isolation.custom"": ""自定义目录"",
  ""version.effective_dir"": ""有效工作目录"",
  ""version.mod_loader"": ""模组加载器"",
  ""version.install_loader"": ""安装加载器"",
  ""version.lock"": ""版本锁定"",
  ""version.lock.desc"": ""锁定后阻止自动更新覆盖该版本"",
  ""version.resolution"": ""分辨率与窗口"",
  ""version.fullscreen"": ""全屏启动"",
  ""version.java"": ""Java 与性能"",
  ""version.max_memory"": ""最大内存"",
  ""version.min_memory"": ""最小内存"",
  ""version.extra_jvm"": ""额外 JVM 参数"",
  ""mods.manage"": ""模组与资源包管理"",
  ""mods.installed"": ""已安装"",
  ""mods.add"": ""添加"",
  ""mods.remove"": ""移除"",
  ""mods.refresh"": ""刷新"",
  ""mods.check_update"": ""检查更新"",
  ""mods.search"": ""搜索"",
  ""mods.open_folder"": ""打开文件夹"",
  ""resourcepack"": ""资源包"",
  ""shader"": ""光影"",
  ""install.fabric"": ""安装 Fabric"",
  ""install.forge"": ""安装 Forge"",
  ""install.neoforge"": ""安装 NeoForge"",
  ""install.quilt"": ""安装 Quilt"",
  ""crash.view.title"": ""崩溃分析报告"",
  ""crash.keep_mod_hint"": ""请选择要保留的 Mod（其余将重命名为 .disabled）："",
  ""crash.missing_dep_hint"": ""缺失的前置依赖（将自动下载安装）："",
  ""crash.downgrade_cause"": ""崩溃疑似由存档降级引起"",
  ""crash.revert_backup"": ""回滚到备份"",
  ""crash.retry_other"": ""改用其他方式"",
  ""crash.install_original"": ""安装存档原版本"",
  ""crash.downgrade_note"": ""所有操作均不删除原存档：回滚会把当前档另存，原档始终保留在备份目录。"",
  ""crash.causes_label"": ""可能原因："",
  ""crash.suggestions_label"": ""修复建议："",
  ""crash.tab.causes"": ""原因与建议"",
  ""crash.tab.full"": ""完整崩溃报告"",
  ""savecompat.title"": ""存档版本不兼容"",
  ""savecompat.detected"": ""检测到以下存档的版本高于当前要启动的游戏版本："",
  ""savecompat.downgrade"": ""降级这些存档"",
  ""savecompat.install_version"": ""安装对应版本"",
  ""savecompat.ignore"": ""仍要启动（忽略）"",
  ""update.found"": ""发现新版本"",
  ""update.emergency"": ""紧急更新：此版本含重要修复或安全更新，建议立即安装"",
  ""update.later"": ""稍后"",
  ""update.download"": ""下载更新"",
  ""update.download_all"": ""全部更新"",
  ""update.gui_only"": ""只更新界面"",
  ""update.gui_only_tip"": ""只更新启动器界面，不下载命令行工具（体积更小）"",
  ""update.fetching_cli"": ""正在下载命令行工具…"",
  ""update.downloading_cli_pct"": ""命令行工具下载中 {0}%…"",
  ""update.preparing"": ""准备下载…"",
  ""lbl.traditional"": ""繁體中文"",
  ""update.version_line"": ""当前 {0} → 最新 {1}"",
  ""update.emgent_note"": ""（紧急更新，请尽快安装）"",
  ""update.recommend_note"": ""（建议立即更新）"",
  ""update.fetching"": ""正在通过内置下载器获取更新包…"",
  ""update.applying"": ""下载完成，正在应用更新…"",
  ""update.cancelled"": ""已取消更新"",
  ""update.failed"": ""更新失败：{0}"",
  ""update.downloading_pct"": ""下载中… {0}%"",
  ""update.no_changelog"": ""（无法获取更新日志，请点击下方「下载更新」在发布页查看详情）"",
  ""settings.music_autoduck"": ""游戏启动时音乐自动降音量"",
  ""settings.music_resume"": ""启动器启动时自动续播上次音乐"",
  ""settings.default_isolation"": ""新建版本默认隔离："",
  ""settings.isolation_shared_label"": ""共享（同一 .minecraft）"",
  ""settings.isolation_auto_label"": ""隔离（各自 versions/<id>）"",
  ""settings.game_dir_label"": ""游戏目录："",
  ""settings.game_dir_tip"": ""Minecraft 游戏目录（.minecraft）。留空表示使用系统默认目录。"",
  ""settings.memory_mb"": ""内存(MB)："",
  ""settings.game_volume"": ""游戏降音量"",
  ""settings.game_volume_tip"": ""开启后每次启动游戏都会把音量在原有基础上降低右侧百分比（不会改动 options.txt）"",
  ""settings.offline_username"": ""离线用户名："",
  ""settings.jvm_args"": ""JVM 参数："",
  ""settings.crash_autorepair_label"": ""崩溃自动修复："",
  ""settings.java_source"": ""Java 源："",
  ""settings.java_temurin"": ""Temurin"",
  ""settings.java_oracle"": ""Oracle"",
  ""settings.missing_dep_label"": ""缺失前置："",
  ""settings.on_label"": ""开启"",
  ""settings.hud_label"": ""HUD 叠加显示"",
  ""settings.global_hotkeys"": ""全局快捷键"",
  ""settings.global_hotkeys_tip"": ""Ctrl+Alt+Enter 快速启动上次游玩的版本；Ctrl+Alt+M 呼出 / 收起启动器窗口（系统级，启动器不在前台也生效）"",
  ""settings.hud_config"": ""HUD 配置"",
  ""settings.hud_fields"": ""显示内容"",
  ""settings.hud_anchor"": ""停靠位置"",
  ""settings.hud_anchor_tl"": ""左上"",
  ""settings.hud_anchor_tr"": ""右上"",
  ""settings.hud_anchor_bl"": ""左下"",
  ""settings.hud_anchor_br"": ""右下"",
  ""settings.hud_font_size"": ""字号"",
  ""settings.hud_opacity"": ""不透明度"",
  ""settings.hud_refresh"": ""刷新间隔"",
  ""settings.hud_margin"": ""屏幕边距"",
  ""settings.hud_click_through"": ""鼠标穿透"",
  ""settings.hud_foreground_only"": ""仅游戏前台显示"",
  ""settings.hud_background"": ""半透明背板"",
  ""settings.hud_reset"": ""恢复默认"",
  ""settings.hud_tip"": ""提示：直接拖动游戏内的 HUD 即可自定义位置，拖动后自动记为「自定义位置」。勾选「鼠标穿透」后 HUD 不拦截鼠标操作。"",
  ""settings.hud_field_fps"": ""帧率"",
  ""settings.hud_field_memory"": ""内存"",
  ""settings.hud_field_cpu"": ""CPU"",
  ""settings.hud_field_ping"": ""延迟"",
  ""settings.hud_field_coords"": ""坐标"",
  ""settings.hud_field_biome"": ""生物群系"",
  ""settings.hud_field_gametime"": ""游戏内时间"",
  ""settings.hud_field_session"": ""本次时长"",
  ""settings.launch_compat_label"": ""启动前存档兼容性检测"",
  ""settings.download_source"": ""下载源优先："",
  ""settings.cf_section"": ""CurseForge 接入"",
  ""settings.ms_login_title"": ""微软登录"",
  ""settings.ms_code_copied"": ""设备代码 {0} 已复制到剪贴板，直接粘贴到浏览器即可"",
  ""settings.cf_key"": ""API Key"",
  ""settings.cf_key_hint"": ""留空则使用启动器内置 Key（构建时注入）；也可用启动参数 --curseforge-key 覆盖"",
  ""settings.cf_root"": ""API Root"",
  ""settings.cf_root_hint"": ""留空使用官方 https://api.curseforge.com，可填第三方镜像地址"",
  ""settings.cf_test"": ""测试连接"",
  ""settings.cf_tip"": ""CurseForge 用于「下载 → 整合包」的在线来源与 Mod 指纹匹配。作者关闭第三方分发的项目无法自动下载，启动器会提示前往官网手动获取。"",
  ""settings.cf_status_disabled"": ""已关闭"",
  ""settings.cf_status_user"": ""正在使用自定义 Key"",
  ""settings.cf_status_builtin"": ""正在使用内置 Key"",
  ""settings.cf_status_none"": ""未配置 Key，CurseForge 源不可用"",
  ""settings.cf_testing"": ""正在测试 CurseForge 连接…"",
  ""settings.cf_test_ok"": ""CurseForge 连接正常"",
  ""settings.cf_test_fail"": ""CurseForge 连接失败：{0}"",
  ""settings.cf_test_unknown"": ""未知原因"",
  ""settings.mirror_first"": ""镜像优先"",
  ""settings.official_first"": ""官方优先"",
  ""settings.max_concurrent"": ""最大并发下载："",
  ""settings.autorepair_rp_label"": ""进服时自动修复资源包问题"",
  ""settings.recommend_master"": ""推荐总开关："",
  ""settings.recommend_enable_label"": ""启用推荐"",
  ""settings.account_mgmt"": ""账号管理"",
  ""settings.ms_client_id"": ""Microsoft client_id："",
  ""settings.ms_client_id_tip"": ""填写你的 Azure 应用 client_id（可选）；留空则使用内置默认 client_id。设备代码流无需配置任何回跳地址。"",
  ""settings.ms_devicecode_desc"": ""微软登录采用设备代码流（device code flow），无需配置任何回跳地址（redirect_uri），兼容性最好。可留空使用内置默认 client_id；如需使用自己的 Azure 应用，填入对应 client_id 即可。保存设置后点击「登录微软」，按弹窗提示在浏览器输入设备代码完成登录。"",
  ""settings.set_current"": ""设为当前"",
  ""settings.authlib_title"": ""Authlib-Injector 账号"",
  ""settings.authlib_desc"": ""外置登录：填入第三方 Authlib-Injector 服务器地址，使用该系统账号（邮箱+密码）登录，可绕过微软官方验证。仅在你信任的私有/社区服务器下使用。微软登录失败多为 Azure 应用 client_id 未配置或被拒，需检查凭据。"",
  ""settings.authlib_hint1"": ""① 服务器地址：第三方 Authlib-Injector 的 URL，如 https://auth.example.com"",
  ""settings.authlib_hint2"": ""② 邮箱：该服务器的账号邮箱"",
  ""settings.authlib_hint3"": ""③ 密码：对应邮箱密码"",
  ""settings.bg_image"": ""背景图片："",
  ""settings.show_borders"": ""显示控件边框"",
  ""settings.follow_system_theme"": ""跟随系统主题"",
  ""settings.follow_system_theme_tip"": ""开启后启动器亮 / 暗随 Windows 系统主题自动切换"",
  ""settings.ui_style"": ""界面风格"",
  ""settings.ui_style_tip"": ""切换整体界面观感；安卓式会把主导航移到底部"",
  ""settings.ui_style_standard"": ""标准（四色索引贴）"",
  ""settings.ui_style_android"": ""简约安卓式"",
  ""settings.ui_style_glass"": ""简约毛玻璃"",
  ""settings.ui_style_dynamic"": ""灵动"",
  ""settings.ui_style_switched"": ""已切换到"",
  ""settings.ui_style_pick"": ""选择"",
  ""settings.ui_style_inuse"": ""使用中"",
  ""settings.window_bg_color"": ""窗口背景色"",
  ""settings.glass_blur"": ""显示毛玻璃效果"",
  ""settings.glass_opacity"": ""毛玻璃不透明度"",
  ""settings.hide_sidebar_labels"": ""隐藏侧边栏图标标签"",
  ""settings.touch_mode"": ""触屏模式"",
  ""settings.touch_mode_tip"": ""开启后随游戏进程显示虚拟按键面板（默认含方向键与跳跃键）"",
  ""settings.touch_config"": ""虚拟按键布局"",
  ""settings.touch_edit"": ""编辑布局"",
  ""settings.touch_reset"": ""恢复默认"",
  ""settings.touch_tip"": ""在编辑器中拖动圆形按键调整位置，可增删按键、修改对应键码与大小；面板随游戏启动显示、游戏退出自动关闭。"",
  ""settings.autoupdate_check"": ""启动时自动检查更新"",
  ""settings.license_github"": ""开源许可 · GitHub 仓库"",
  ""settings.reset_default"": ""恢复默认"",
  ""settings.auto_detect"": ""自动检测"",
  ""settings.open_label"": ""打开"",
  ""ai.m01"": ""燧石 AI 助手"",
  ""ai.m02"": ""我可以帮你分析崩溃日志、推荐 Mod、翻译描述、生成年度总结，也可以直接聊天。"",
  ""ai.m03"": ""自动分析最近一次崩溃日志"",
  ""ai.m04"": ""把英文 Mod 描述译为中文"",
  ""ai.m05"": ""配装推荐"",
  ""ai.m06"": ""按你的玩法偏好推荐 Mod"",
  ""ai.m07"": ""年度总结"",
  ""ai.m08"": ""汇总你这一年的游玩记录"",
  ""ai.m09"": ""输入你的问题…（可粘贴崩溃日志）"",
  ""ai.m10"": ""发送"",
  ""ai.m11"": ""开启后可选本地部署或外部 API，用于崩溃分析、Mod 推荐翻译等场景。"",
  ""ai.m12"": ""外部 API（填 Key 即用，零下载、零占用）"",
  ""ai.m13"": ""本地部署（自动安装 Ollama + 轻量模型）"",
  ""ai.m14"": ""本地部署（Ollama）"",
  ""ai.m15"": ""一键安装 Ollama"",
  ""ai.m16"": ""服务状态："",
  ""ai.m17"": ""外部 API（OpenAI 兼容）"",
  ""ai.m18"": ""API 端点"",
  ""ai.m19"": ""模型名"",
  ""ai.m20"": ""AI 功能"",
  ""ai.m21"": ""崩溃日志智能解读"",
  ""ai.m22"": ""生成推荐理由"",
  ""ai.m23"": ""Mod 描述翻译"",
  ""ai.m24"": ""清空对话"",
  ""annual.m24"": ""年份"",
  ""annual.m25"": ""年度称号："",
  ""annual.m26"": ""复制 Token"",
  ""annual.m27"": ""导出 Markdown"",
  ""annual.m28"": ""总时长（小时）"",
  ""annual.m29"": ""启动次数"",
  ""annual.m30"": ""活跃天数"",
  ""annual.m31"": ""最长连续（天）"",
  ""annual.m32"": ""单次最久"",
  ""annual.m33"": ""月度分布（小时）"",
  ""annual.m34"": ""版本排行（小时）"",
  ""annual.m35"": ""一句话解读"",
  ""annual.m36"": ""分享 Token（完全离线，复制发给好友，对方粘贴即可看你的年度报告）"",
  ""annual.m37"": ""把好友的分享 Token 粘贴到这里"",
  ""annual.m38"": ""好友的"",
  ""annual.m39"": ""年度报告（称号："",
  ""annual.m40"": ""总时长 "",
  ""annual.m41"": "" 小时 · "",
  ""annual.m42"": "" 次启动 · 活跃 "",
  ""annual.m43"": "" 天 · 崩溃 "",
  ""annual.m44"": "" 次"",
  ""backup.m45"": ""备份策略"",
  ""backup.m46"": ""存储路径"",
  ""backup.m47"": ""相对路径按游戏目录解析；也可填绝对路径备份到移动硬盘"",
  ""backup.m48"": ""浏览…"",
  ""backup.m49"": ""打开目录"",
  ""backup.m50"": ""定时备份"",
  ""backup.m51"": ""每来源保留"",
  ""backup.m52"": ""超出份数时自动删除最旧的（0 = 不限）"",
  ""backup.m53"": ""份"",
  ""backup.m54"": ""自动备份保留"",
  ""backup.m55"": ""超过天数的自动备份会被清理（0 = 不按时间清理）；手动备份不受影响"",
  ""backup.m56"": ""天"",
  ""backup.m57"": ""恢复前自动备份当前状态"",
  ""backup.m58"": ""启动游戏前自动备份存档"",
  ""backup.m59"": ""保存策略"",
  ""backup.m60"": ""备份来源"",
  ""backup.m61"": ""备注"",
  ""backup.m62"": ""可选，方便日后辨认这份备份"",
  ""backup.m63"": ""立即备份"",
  ""backup.m64"": ""来源"",
  ""backup.m65"": ""类型"",
  ""backup.m66"": ""创建时间"",
  ""backup.m67"": ""大小"",
  ""backup.m68"": ""文件数"",
  ""backup.m69"": ""恢复选中"",
  ""backup.m70"": ""删除选中"",
  ""backup.m71"": ""按策略清理"",
  ""cmd.m72"": ""常用语法表"",
  ""cmd.m73"": ""插入"",
  ""cmd.m74"": ""命令拼接 / 编辑"",
  ""crash.m75"": ""最近的崩溃报告"",
  ""crash.m76"": ""分析与建议"",
  ""crash.m77"": ""打开完整分析报告"",
  ""datapack.m78"": ""存档"",
  ""datapack.m79"": ""目标版本"",
  ""datapack.m80"": ""填写如 1.21，用于 pack_format 告警；留空则跳过该检查"",
  ""datapack.m81"": ""扫描"",
  ""datapack.m82"": ""刷新存档"",
  ""datapack.m83"": ""更新规则库"",
  ""datapack.m84"": ""恢复内置"",
  ""datapack.m85"": ""生效："",
  ""datapack.m86"": ""　被覆盖："",
  ""datapack.m87"": ""跳转到数据包"",
  ""datapack.m88"": ""数据包（按加载顺序）"",
  ""datapack.m89"": ""　缺 pack.mcmeta"",
  ""datapack.m90"": ""格式告警"",
  ""datapack.m91"": ""处理建议："",
  ""dev.m92"": ""命令速查"",
  ""dev.m93"": ""Mod 骨架"",
  ""dev.m94"": ""资源包创建"",
  ""dev.m95"": ""Mod 名称"",
  ""dev.m96"": ""版本号"",
  ""dev.m97"": ""加载器"",
  ""dev.m98"": ""游戏版本"",
  ""dev.m99"": ""包名（如 com.example.mymod）"",
  ""dev.m100"": ""生成项目骨架"",
  ""dev.m101"": ""新手教程"",
  ""dev.m102"": ""1. 输入名称和 pack_format（1.21=34, 1.20=15, 1.19=9）"",
  ""dev.m103"": ""2. 生成后在 assets/minecraft 下放入纹理/模型/声音文件"",
  ""dev.m104"": ""3. 打包 zip 或直接放到 .minecraft/resourcepacks"",
  ""dev.m105"": ""资源包名称"",
  ""dev.m106"": ""pack_format（MC版本对应的格式号）"",
  ""dev.m107"": ""生成资源包"",
  ""dl.m108"": ""下载中心 (Modrinth)"",
  ""dl.m109"": ""搜索关键词"",
  ""dl.m110"": ""加入队列"",
  ""dl.m111"": ""开始下载队列"",
  ""dl.m112"": ""清空已完成"",
  ""dl.m113"": ""该地图附带资源包 / 光影，可在详情窗一并安装"",
  ""dl.m114"": ""含附加资源"",
  ""dl.m115"": ""整合包来源"",
  ""dl.m116"": ""版本类型"",
  ""dl.m117"": ""全部"",
  ""dl.m118"": ""正式版"",
  ""dl.m119"": ""快照"",
  ""dl.m120"": ""旧版"",
  ""dl.m121"": ""最新发布"",
  ""dl.m122"": ""最多浏览"",
  ""dl.m123"": ""上一页"",
  ""dl.m124"": ""第 "",
  ""dl.m125"": "" 页"",
  ""dl.m126"": ""下一页"",
  ""dl.m127"": ""作者"",
  ""dl.m128"": ""浏览"",
  ""dl.m129"": ""点赞"",
  ""dl.m130"": ""评分"",
  ""dl.m131"": ""地图站页面"",
  ""dl.m132"": ""下载资源包 / 光影并自动分发到 resourcepacks、shaderpacks"",
  ""dl.m133"": ""下载到存档"",
  ""dl.m134"": ""选择版本"",
  ""dl.m135"": ""隔离安装（独立 versions/<包名> 目录）"",
  ""dl.m136"": ""来源页面"",
  ""dl.m137"": ""版本数"",
  ""dl.m138"": ""AI 翻译"",
  ""dl.m139"": ""未启用 AI 助手时不可用"",
  ""dl.m140"": ""项目页面"",
  ""dl.m141"": ""安装所选版本"",
  ""dl.m142"": ""安装 Minecraft 版本"",
  ""dl.m143"": ""选择加载器（可不选）"",
  ""dl.m144"": ""原版（不安装加载器）"",
  ""dl.m145"": ""Fabric（自动配对最新 Fabric API）"",
  ""game.m146"": ""📦 版本库"",
  ""game.m147"": ""打开版本列表大页"",
  ""game.m148"": ""未发现局域网世界，请在游戏内开启「对局域网开放」后点击刷新。"",
  ""game.m149"": ""服务器列表为空。"",
  ""game.m150"": ""刷新推荐"",
  ""game.m151"": ""依赖"",
  ""game.m152"": ""暂无推荐，可点击「刷新推荐」。"",
  ""game.m153"": ""崩溃次数（年）"",
  ""game.m154"": ""查看"",
  ""game.m155"": ""今日可查看年度游玩统计"",
  ""home.m156"": ""选择版本："",
  ""home.m157"": ""游玩统计"",
  ""home.m158"": ""累计时长"",
  ""home.m159"": ""为你推荐"",
  ""home.m160"": ""依赖补全"",
  ""install.m161"": ""安装新版本"",
  ""install.m162"": ""类型："",
  ""install.m163"": ""版本："",
  ""log.m164"": ""仅错误"",
  ""map.m165"": ""安装地图"",
  ""moddev.m166"": ""显示名称"",
  ""moddev.m167"": ""目标 MC 版本"",
  ""moddev.m168"": ""目标目录"",
  ""moddev.m169"": ""生成 Mod 骨架"",
  ""modpack.m170"": ""导出整合包"",
  ""modpack.m171"": ""显示名称（可选）"",
  ""modpack.m172"": ""包含 Mods"",
  ""modpack.m173"": ""包含 Config"",
  ""modpack.m174"": ""包含 资源包"",
  ""modpack.m175"": ""包含 光影包"",
  ""modpack.m176"": ""包含 存档"",
  ""modpack.m177"": ""导出…"",
  ""modpack.m178"": ""导入整合包"",
  ""modpack.m179"": ""支持 Modrinth .mrpack 整合包。"",
  ""modpack.m180"": ""选择文件并导入…"",
  ""music.m181"": ""音源："",
  ""music.m182"": ""本地文件夹"",
  ""music.m183"": ""在线流媒体"",
  ""music.m184"": ""MC 原声"",
  ""music.client_mode"": ""本地客户端"",
  ""music.client_title"": ""本地客户端模式"",
  ""music.client_desc"": ""音乐由外部客户端播放，启动器不再输出音频。切换到其它音源后，若客户端持续暂停超过设定时长会自动结束它。"",
  ""music.client_choose"": ""选择程序"",
  ""music.client_launch"": ""启动客户端"",
  ""music.client_lyric_hint"": ""客户端模式下歌词按歌名从在线歌词库匹配，若客户端正在播放的歌名不同则不会显示。"",
  ""music.m185"": ""加载音乐文件夹"",
  ""music.m186"": ""在线流媒体地址（直接播放链接）"",
  ""music.m187"": ""预设："",
  ""music.m188"": ""扫描 MC 原声"",
  ""music.m189"": ""删除曲目"",
  ""music.col_name"": ""名称"",
  ""music.col_title"": ""标题"",
  ""music.col_artist"": ""艺术家"",
  ""music.col_album"": ""专辑"",
  ""music.col_duration"": ""时长"",
  ""nbt.m190"": ""打开…"",
  ""nbt.m191"": ""另存为…"",
  ""nbt.m192"": ""导出文本"",
  ""nbt.m193"": ""保存时先把原文件复制一份 .bak"",
  ""nbt.m194"": ""保存前自动备份"",
  ""nbt.m195"": ""存档的 level.dat 快捷入口"",
  ""nbt.m196"": ""全部展开"",
  ""nbt.m197"": ""全部折叠"",
  ""nbt.m198"": ""选中节点"",
  ""nbt.m199"": ""路径"",
  ""nbt.m200"": ""值（仅标量可编辑）"",
  ""nbt.m201"": ""应用修改"",
  ""nbt.m202"": ""新增 / 重命名"",
  ""nbt.m203"": ""名称"",
  ""nbt.m204"": ""新增子标签"",
  ""nbt.m205"": ""重命名"",
  ""nbt.m206"": ""删除选中标签"",
  ""net.m207"": ""重新检测"",
  ""perf.m208"": ""运行实例与性能"",
  ""perf.m209"": ""系统 CPU 占用"",
  ""perf.m210"": ""内存占用"",
  ""perf.m211"": ""运行实例"",
  ""perf.m212"": ""CPU 逻辑核数"",
  ""perf.m213"": ""可用内存"",
  ""perf.m214"": ""累计游玩(分钟)"",
  ""perf.m215"": ""启动时间"",
  ""clean.m216"": ""清理选中"",
  ""clean.m217"": ""直接删除（不可还原）"",
  ""saves.m218"": ""存档管理"",
  ""saves.m219"": ""目标游戏版本："",
  ""saves.m220"": ""扫描兼容性"",
  ""saves.m221"": ""扫描损坏"",
  ""saves.m222"": ""(§二.4 检测 / §三 降级与回滚 / 损坏检测只检测不修复)"",
  ""saves.m223"": ""当前版本："",
  ""saves.m224"": ""备份："",
  ""saves.m225"": ""损坏检测："",
  ""saves.m226"": ""提取种子"",
  ""saves.m227"": ""备份"",
  ""saves.m228"": ""降级"",
  ""saves.m229"": ""回滚"",
  ""screenshot.m230"": ""打包分享"",
  ""server.m231"": ""容量上限"",
  ""server.m232"": ""超过该上限后按 LRU 自动淘汰最旧的"",
  ""server.m233"": ""导出到文件夹"",
  ""server.m234"": ""打开缓存目录"",
  ""server.m235"": ""清空"",
  ""server.m236"": ""共 "",
  ""server.m237"": "" 份缓存，占用 "",
  ""server.m238"": "" MB（累计命中 "",
  ""server.m239"": "" 次），容量上限 "",
  ""server.m240"": ""文件名"",
  ""server.m241"": ""来源服务器"",
  ""server.m242"": ""命中"",
  ""server.m243"": ""最后使用"",
  ""server.m244"": ""还没有缓存任何服务器资源包。进服时如果服务器下发了资源包，会自动出现在这里。"",
  ""server.m245"": ""导出到资源包目录"",
  ""shader.m246"": ""光影配置参数"",
  ""shader.m247"": ""+ 添加参数"",
  ""shader.m248"": ""参数ID（如shadowRes）"",
  ""shader.m249"": ""参数值（如2048）"",
  ""shader.m250"": ""完整 Token"",
  ""shader.m251"": ""导入 Token"",
  ""shader.m252"": ""从 Token 解析"",
  ""shader.m253"": ""载入"",
  ""shortcut.m254"": ""创建到桌面"",
  ""skin.m255"": ""身体部位"",
  ""skin.m256"": ""工具"",
  ""skin.m257"": ""对称绘制"",
  ""skin.m258"": ""橡皮擦"",
  ""skin.m259"": ""画笔大小"",
  ""skin.m260"": ""← 撤销"",
  ""skin.m261"": ""重做 →"",
  ""skin.m262"": ""清空全部"",
  ""skin.m263"": ""导入 PNG"",
  ""skin.m264"": ""导出 PNG"",
  ""skin.m265"": ""应用到离线账号"",
  ""skin.m266"": ""2D 编辑"",
  ""skin.m267"": ""3D 预览"",
  ""skin.m268"": ""纤细手臂"",
  ""skin.m269"": ""缩放"",
  ""skin.m270"": ""填充"",
  ""skin.m271"": ""64x64 预览"",
  ""skin.m272"": ""当前颜色"",
  ""skin.m273"": ""调色板"",
  ""skin.m274"": ""玩家名："",
  ""skin.m275"": ""查询"",
  ""skin.m276"": ""拖动旋转 · 滚轮缩放"",
  ""skin.m277"": ""输入 Minecraft 正版用户名以获取皮肤"",
  ""skin.m304"": ""取色器（含完整色板）"",
  ""skin.m305"": ""副色 ← 主色"",
  ""ver.m278"": ""← 返回"",
  ""ver.m279"": ""返回上一页"",
  ""ver.m280"": ""已安装版本"",
  ""ver.m281"": ""版本设置…"",
  ""ver.search"": ""检索"",
  ""ver.filter_all"": ""全部"",
  ""ver.filter_loader"": ""加载器"",
  ""ver.filter_dir"": ""目录"",
  ""ver.filter_content"": ""内容"",
  ""ver.filter_missing"": ""仅缺前置"",
  ""ver.filter_reset"": ""清空检索"",
  ""ver.iso"": ""隔离"",
  ""ver.shared"": ""共享"",
  ""ver.content_mods"": ""Mod"",
  ""ver.content_rp"": ""材质包"",
  ""ver.content_sp"": ""光影"",
  ""ver.content_dp"": ""数据包"",
  ""ver.content_saves"": ""存档"",
  ""ver.loader_quilt"": ""Quilt"",
  ""ver.loader_neoforge"": ""NeoForge"",
  ""version.m282"": ""① 基础信息"",
  ""version.m283"": ""删除该版本"",
  ""version.m284"": ""版本 Id："",
  ""version.m285"": ""  ｜  类型："",
  ""version.m286"": ""  ｜  基版本："",
  ""version.m287"": ""③ 模组加载器"",
  ""version.m288"": ""当前："",
  ""version.m289"": ""加载器会安装为新实例（基于上方基版本），请在版本列表切换到它。"",
  ""version.m290"": ""④ 隔离与工作目录"",
  ""version.m291"": ""有效工作目录："",
  ""version.m292"": ""⑤ Java 与性能"",
  ""version.m293"": ""额外 JVM 参数（每行一个，追加在全局参数之后）"",
  ""version.jvm_presets"": ""常用参数（勾选即可）"",
  ""version.jvm_custom"": ""自定义参数（每行一条）"",
  ""version.jvm_preview"": ""最终生效参数（只读预览）"",
  ""version.jvm_g1gc"": ""启用 G1 垃圾回收器"",
  ""version.jvm_g1gc_tip"": ""适合大内存，停顿更可控（Java 8+ 推荐）"",
  ""version.jvm_strdedup"": ""字符串去重"",
  ""version.jvm_strdedup_tip"": ""需配合 G1 使用，减少重复字符串占用的内存"",
  ""version.jvm_dis_explicit"": ""禁用显式 GC"",
  ""version.jvm_dis_explicit_tip"": ""忽略 System.gc() 调用，避免 Mod 触发卡顿"",
  ""version.jvm_pretouch"": ""启动时预分配内存"",
  ""version.jvm_pretouch_tip"": ""牺牲一点启动时间换取运行时更稳定的内存分配"",
  ""version.jvm_parref"": ""并行引用处理"",
  ""version.jvm_parref_tip"": ""缩短 GC 中引用处理阶段的耗时"",
  ""version.jvm_log4j"": ""关闭 Log4j2 消息查找"",
  ""version.jvm_log4j_tip"": ""屏蔽 Log4j2 漏洞利用路径（旧版本推荐）"",
  ""version.jvm_fml_cert"": ""Forge：忽略无效证书"",
  ""version.jvm_fml_cert_tip"": ""修复部分 Forge 版本因证书校验失败无法启动"",
  ""version.jvm_fml_patch"": ""Forge：忽略补丁差异"",
  ""version.jvm_fml_patch_tip"": ""修复 Forge 补丁校验不一致导致的启动失败"",
  ""version.jvm_encoding"": ""强制 UTF-8 编码"",
  ""version.jvm_encoding_tip"": ""避免中文路径 / 聊天内容乱码"",
  ""version.jvm_maxpause"": ""GC 最大停顿目标（毫秒）"",
  ""version.jvm_maxpause_tip"": ""G1 会尽量把单次 GC 停顿控制在该值内"",
  ""version.jvm_gcthreads"": ""GC 并行线程数"",
  ""version.jvm_gcthreads_tip"": ""一般不超过 CPU 核心数"",
  ""version.jvm_ihop"": ""并发 GC 触发占用率（%）"",
  ""version.jvm_ihop_tip"": ""堆占用达到该百分比时启动并发标记"",
  ""version.m294"": ""⑥ 分辨率与窗口"",
  ""version.m295"": ""宽"",
  ""version.m296"": ""高"",
  ""version.m297"": ""⑦ 模组与资源包管理"",
  ""version.m298"": ""搜索 Modrinth"",
  ""version.m299"": ""⑧ 版本锁定"",
  ""version.m300"": ""锁定该版本（阻止自动更新覆盖，并阻止安装加载器 / 增删 Mod）"",
  ""version.m301"": ""⑨ 账号绑定"",
  ""version.m302"": ""绑定账号"",
  ""version.m303"": ""清除绑定"",
  ""install.start"": ""开始安装"",
  ""install.cancel"": ""取消安装"",
  ""install.progress"": ""安装进度"",
  ""install.refresh"": ""刷新"",
  ""install.done"": ""安装完成，版本列表已刷新"",
  ""install.cancelled"": ""已取消安装"",
  ""install.failed"": ""安装失败"",
  ""map.zip_hint"": ""地图 zip 会自动安装到 saves/，并检测根目录前缀。"",
  ""shortcut.title"": ""版本快捷方式"",
  ""shortcut.display_name"": ""显示名称（可选）"",
  ""shortcut.tip"": ""提示：双击快捷方式即以该版本启动游戏（向启动器传入 --launch <版本>）。"",
  ""ai.ollama_pre"": ""Ollama 已安装（"",
  ""ai.ollama_post"": ""）"",
  ""annual.month"": ""月"",
  ""dl.total_pre"": ""共 "",
  ""dl.total_post"": "" 项"",
  ""home.minutes"": "" 分钟"",
  ""rec.downloads"": "" 下载"",
  ""skin.model_pre"": ""模型："",
  ""achieve.title"": ""成就"",
  ""achieve.purple"": ""紫色成就：{0}"",
  ""afk.add_action"": ""+ 添加动作"",
  ""afk.action_type"": ""动作类型"",
  ""afk.param"": ""参数"",
  ""afk.plaintext_tip"": ""明文文本（将自动转为 base64）"",
  ""afk.run"": ""运行"",
  ""afk.stop"": ""停止"",
  ""afk.token"": ""令牌"",
  ""afk.save_workflow"": ""保存工作流"",
  ""afk.import_token"": ""导入令牌"",
  ""afk.import_from_token"": ""从令牌导入"",
  ""afk.saved_workflows"": ""已保存工作流"",
  ""afk.load"": ""载入"",
  ""afk.delete_workflow_tip"": ""删除此工作流"",
  ""afk.picker_title"": ""选择动作类型"",
  ""afk.picker_hint"": ""点击一种动作以添加到工作流"",
  ""afk.type_f"": ""功能键"",
  ""afk.type_d"": ""延时"",
  ""afk.type_l"": ""长按"",
  ""afk.type_k"": ""虚拟键"",
  ""afk.type_c"": ""左键连点"",
  ""afk.type_r"": ""右键连点"",
  ""afk.type_m"": ""鼠标移动"",
  ""afk.type_g"": ""按住"",
  ""afk.type_j"": ""随机等待"",
  ""afk.type_s"": ""滚轮"",
  ""afk.type_t"": ""输入文本"",
  ""afk.type_u"": ""松开"",
  ""afk.type_loop"": ""循环"",
  ""afk.type_e"": ""按键"",
  ""cmd.copy_clipboard"": ""复制命令"",
  ""server.name"": ""服务器名称"",
  ""server.address"": ""服务器地址"",
  ""server.name_empty"": ""服务器名称不能为空"",
  ""server.addr_empty"": ""服务器地址不能为空"",
  ""settings.theme"": ""主题"",
  ""settings.isolation_tip"": ""新建版本时自动套用的隔离模式：共享=同一 .minecraft；隔离=各自 versions/<id>"",
  ""settings.ms_client_id_note"": ""（留空使用内置默认 client_id）"",
  ""settings.save_settings"": ""保存设置"",
  ""settings.font_scaling"": ""字体缩放："",
  ""mods.mgmt_title"": ""Mod 管理"",
  ""mods.missing_dep_label"": ""缺失依赖："",
  ""mods.required_tag"": ""[必需]"",
  ""mods.conflict_label"": ""冲突："",
  ""mods.installed_tag"": "" 已安装 "",
  ""mods.conflict_range_tag"": "" 冲突范围 "",
  ""mods.col_name"": ""名称"",
  ""mods.col_modid"": ""Mod ID"",
  ""mods.col_version"": ""版本"",
  ""mods.col_loader"": ""加载器"",
  ""mods.col_latest"": ""最新"",
  ""mods.select_label"": ""选择："",
  ""mods.select_all"": ""全选"",
  ""mods.select_none"": ""全不选"",
  ""mods.select_outdated"": ""选有更新"",
  ""mods.selected_count"": ""已选 "",
  ""mods.verify_selected"": ""核验勾选"",
  ""mods.verify_all"": ""核验全部"",
  ""mods.update_selected"": ""更新勾选"",
  ""mods.update_all"": ""更新全部"",
  ""mods.update_one"": ""更新"",
  ""mods.enable"": ""启用"",
  ""mods.disable"": ""禁用"",
  ""mods.toggle"": ""启/禁"",
  ""mods.uninstall_selected"": ""卸载勾选"",
  ""mods.col_state"": ""状态"",
  ""mods.col_verify"": ""核验结果"",
  ""mods.col_action"": ""操作"",
  ""mods.uninstall"": ""卸载"",
  ""seasonal.midautumn.greeting.title"": ""中秋快乐"",
  ""seasonal.midautumn.greeting.message"": ""愿你月圆人团圆，游戏顺利～（可在设置中关闭节日特效）"",
  ""seasonal.midautumn.name"": ""中秋节"",
  ""seasonal.midautumn.splash.title"": ""中秋快乐"",
  ""seasonal.midautumn.splash.message"": ""愿此夜月色常伴你的每一次冒险"",
  ""seasonal.hub_title"": ""节日中心"",
  ""seasonal.easter_egg"": ""🥚 彩蛋小游戏"",
  ""seasonal.easter_egg_tip"": ""抓假方块：它冒出来就点它，点空扣分 —— 全年还能用方向键秘籍召唤"",
  ""tool.lanlink"": ""局域网联动"",
  ""nat.label"": ""网络类型"",
  ""nat.probing"": ""测定中…"",
  ""nat.not_probed"": ""尚未测定"",
  ""nat.eim"": ""锥形 NAT — 对方可以直接连到你"",
  ""nat.ad"": ""依赖地址的 NAT — 直连成功率偏低"",
  ""nat.apd"": ""对称型 NAT — 几乎无法直连"",
  ""nat.unknown"": ""未能判定（可以照常试一次）"",
  ""nat.udp_blocked"": ""UDP 出不去 — STUN 服务器全部无回应"",
  ""nat.stun"": ""STUN 服务器"",
  ""nat.stun_tip"": ""留空则自动选择。校园网、公司网、部分宽带下常常只有自己指定的那台才通，填写格式 host:3478，多个用逗号隔开"",
  ""nat.fallback_tip"": ""直连走不通时的退路：两台机装同一个组网工具（ZeroTier、蒲公英、Radmin VPN 等），组好虚拟局域网后回到本页上面，用「局域网联动」——那套不依赖 NAT 穿透。"",
  ""p2p.title"": ""广域网直连"",
  ""p2p.section_tip"": ""不在同一个局域网时用这个：双方交换一次握手码，然后 UDP 打洞直连。全程不经任何服务器，代价是两边都不在同一个 NAT 后面且路由器不允许时才打得通。"",
  ""p2p.host_btn"": ""① 我有世界，生成邀请码"",
  ""p2p.join_btn"": ""② 我收到了对方的邀请码"",
  ""p2p.host_btn_tip"": ""本机已经开着 Minecraft 并对局域网开放（先点上面的「读取本机端口」）"",
  ""p2p.join_btn_tip"": ""把对方发来的 CHERT2: 那一段粘进下面的输入框"",
  ""p2p.my_ip"": ""我的公网 IP"",
  ""p2p.my_ip_tip"": ""不确定就留空。路由器不支持 UPnP、又没有公网 IPv6 时，手动填才能真正打洞成功"",
  ""p2p.my_code"": ""我的码（发给对方）"",
  ""p2p.copy_code"": ""复制"",
  ""p2p.qr_tip"": ""对方用手机扫这张图最省事；不用截图，直接让他读这段文字也行"",
  ""p2p.fingerprint"": ""校验指纹"",
  ""p2p.fingerprint_tip"": ""和对方核对这两串数字是否一致，防止连到别人身上"",
  ""p2p.peer_code"": ""对方的码"",
  ""p2p.peer_code_host_tip"": ""把对方回给你的「应答码」粘在这里"",
  ""p2p.peer_code_join_tip"": ""把房主发来的「邀请码」粘在这里"",
  ""p2p.connect"": ""开始连接"",
  ""p2p.stop"": ""断开并重置"",
  ""p2p.reconnect"": ""重新连接"",
  ""p2p.reconnect_tip"": ""沿用刚才交换过的码再打一次洞。两边都要点 —— 只有一边发，另一边的 NAT 不会放行。"",
  ""p2p.role_host"": ""房主"",
  ""p2p.role_joiner"": ""加入者"",
  ""p2p.ready_joiner"": ""直连成功！在 Minecraft 的「多人游戏 → 直接连接」里填：{0}"",
  ""p2p.ready_host"": ""隧道已就绪，等待对方接入"",
  ""p2p.ready_hint"": ""直连成功！请在 Minecraft 的「多人游戏 → 直接连接」里填 {0}"",
  ""p2p.need_local_port"": ""先点上面的「读取本机端口」，让启动器知道把对方转到本机的哪个端口"",
  ""p2p.need_choice"": ""先选一下你在这一局里是房主还是加入者"",
  ""p2p.no_code"": ""还没有生成码"",
  ""p2p.code_copied"": ""已复制，发给对方吧"",
  ""p2p.unsupported"": ""本机不支持广域网直连：需要 QUIC（Windows 11 及以上）与 X25519 密钥交换"",
  ""lan.title"": ""局域网联动"",
  ""lan.enable"": ""允许被局域网发现"",
  ""lan.enable_tip"": ""开启后本机监听发现 / 指令端口，同一 Wi-Fi 下的另一台燧石就能找到你"",
  ""lan.service_tip"": ""首次监听会弹 Windows 防火墙提示，点「允许访问」即可；不需要管理员权限。关闭开关即停止监听。"",
  ""lan.my_world"": ""我的世界"",
  ""lan.publish"": ""一键开放"",
  ""lan.publish_tip"": ""向游戏窗口投递 /publish（等价于游戏内的「对局域网开放」）"",
  ""lan.detect"": ""读取本机端口"",
  ""lan.detect_tip"": ""从游戏日志读取已开放的局域网端口"",
  ""lan.copy_share"": ""复制邀请"",
  ""lan.devices"": ""局域网设备"",
  ""lan.manual_ip"": ""对方 IP"",
  ""lan.manual_ip_tip"": ""广播被禁时可手动填对方局域网 IP 定向探测"",
  ""lan.probe"": ""探测"",
  ""lan.pair"": ""配对"",
  ""lan.gen_code"": ""生成配对码"",
  ""lan.input_code"": ""对方配对码"",
  ""lan.do_pair"": ""配对"",
  ""lan.pair_tip"": ""双方各生成一次配对码、互相输入即可建立会话；配对码 10 分钟有效。未配对的设备发来的指令一律拒绝。"",
  ""lan.actions"": ""联动"",
  ""lan.invite"": ""邀请加入我的世界"",
  ""lan.invite_tip"": ""把本机世界地址发给选中设备，对方屏幕上会弹确认框"",
  ""lan.request_open"": ""请对方开放世界"",
  ""lan.request_open_tip"": ""请求对方的 Minecraft 执行 /publish"",
  ""lan.join_addr"": ""世界地址"",
  ""lan.join"": ""加入"",
  ""lan.join_tip"": ""可直接粘贴 CHERT1: 邀请码，或形如 192.168.1.5:52931 的地址"",
  ""lan.invite_title"": ""局域网联机邀请"",
  ""lan.gen_invite"": ""生成邀请码"",
  ""lan.gen_invite_tip"": ""把世界地址 / 设备名 / 配对码打包成一段可复制的文本"",
  ""lan.copy_invite"": ""复制邀请码"",
  ""lan.copy_invite_tip"": ""整段复制（含 CHERT1: 前缀），聊天软件里发给对方"",
  ""lan.copy_code"": ""复制配对码"",
  ""lan.code_none"": ""还没有配对码，先点「生成配对码」"",
  ""lan.code_copied"": ""配对码已复制"",
  ""lan.check"": ""校验码"",
  ""lan.invite_valid"": ""{0} 分钟内有效"",
  ""lan.invite_ready"": ""邀请码已生成，复制整段发给对方（对方在「世界地址」里粘贴即可）"",
  ""lan.invite_copied"": ""邀请码已复制"",
  ""lan.invite_need_endpoint"": ""先点「读取本机端口」拿到本机世界地址"",
  ""lan.invite_stale"": ""世界地址变了，请重新生成邀请码"",
  ""lan.invite_from"": ""来自 {0} 的邀请 · 校验码 {1}"",
  ""lan.err_expired"": ""邀请码已过期（10 分钟），请让对方重新生成"",
  ""lan.err_broken"": ""邀请码不完整或被改动，请让对方重新复制整段"",
  ""lan.err_version"": ""邀请码版本不兼容，双方都升级到最新版启动器"",
  ""lan.err_endpoint"": ""地址格式不对，应为 host:port（如 192.168.1.5:52931）"",
  ""lan.err_endpoint_fmt"": ""地址格式不对：{0}"",
  ""lan.err_generic"": ""邀请码无法识别，请检查是否复制完整"",
  ""lan.detect_scanning"": ""日志里没读到端口，正在监听 3 秒局域网广播…"",
  ""lan.detect_fail"": ""没读到端口。确认游戏里已经「对局域网开放」，或先点「一键开放」。"",
  ""lan.detect_ok"": ""本机世界地址"",
  ""lan.copy_none"": ""还没有可分享的地址"",
  ""lan.copy_done"": ""邀请文本已复制到剪贴板"",
  ""lan.copy_fail"": ""复制失败，请手动选中文本复制"",
  ""lan.join_need_addr"": ""请先填写世界地址或粘贴邀请码"",
  ""lan.joining"": ""正在加入"",
  ""seasonal.hub_loading"": ""节日配置尚未就绪，请稍后刷新"",
  ""seasonal.hub_empty"": ""当前没有可展示的节日内容"",
  ""seasonal.hub_summary"": ""活动 {0} 项 · 服务器 {1} 个 · 推荐 {2} 个"",
  ""seasonal.current"": ""当前节日"",
  ""seasonal.none"": ""当前不在任何节日档期"",
  ""seasonal.events_title"": ""限时活动"",
  ""seasonal.servers_title"": ""节日服务器推荐"",
  ""seasonal.picks_title"": ""节日内容推荐"",
  ""seasonal.events_empty"": ""暂无限时活动"",
  ""seasonal.servers_empty"": ""暂无节日服务器推荐"",
  ""seasonal.picks_empty"": ""暂无节日内容推荐"",
  ""seasonal.open_link"": ""打开"",
  ""seasonal.copy_address"": ""复制地址"",
  ""seasonal.copied"": ""已复制服务器地址"",
  ""seasonal.copy_failed"": ""复制失败"",
  ""seasonal.announcement.title"": ""节日公告"",
  ""seasonal.kind.mod"": ""模组"",
  ""seasonal.kind.modpack"": ""整合包"",
  ""seasonal.kind.resourcepack"": ""材质包"",
  ""seasonal.kind.shader"": ""光影"",
  ""seasonal.kind.version"": ""版本"",
  ""seasonal.kind.other"": ""其他"",
  ""seasonal.effects_hint"": ""节日特效与内容由远程配置驱动，可在「设置 → 外观」中关闭节日特效。"",
  ""settings.seasonal_effects"": ""节日特效"",
  ""settings.java_path_hint"": ""此项仅作为首选 Java；启动器仍会按各版本要求自动避开其不支持的 Java 版本，无需手动切换。"",
  ""settings.use_littleskin"": ""使用 LittleSkin"",
  ""settings.use_littleskin_tip"": ""自动填入 LittleSkin 的外置登录服务器地址"",
  ""settings.authlib_url_ph"": ""服务器地址，如 https://auth.example.com"",
  ""settings.authlib_email_ph"": ""该服务器的账号邮箱""
}";

                        private static string BuiltInEnUS() => @"{
  ""app.title"": ""Chert Launcher"",
  ""app.launcher"": ""Chert Launcher"",
  ""tab.launch"": ""Launch"",
  ""tab.install"": ""Install"",
  ""tab.download"": ""Download"",
  ""tab.settings"": ""Settings"",
  ""tab.crash"": ""Crash Analyzer"",
  ""tab.mods"": ""Mods"",
  ""tab.accounts"": ""Accounts"",
  ""tab.skin"": ""Skin Preview"",
  ""btn.launch"": ""Launch Game"",
  ""btn.install"": ""Install"",
  ""btn.cancel"": ""Cancel"",
  ""btn.save"": ""Save"",
  ""btn.refresh"": ""Refresh"",
  ""btn.search"": ""Search"",
  ""btn.download"": ""Download"",
  ""btn.login"": ""Login"",
  ""btn.logout"": ""Logout"",
  ""btn.add_account"": ""Add Account"",
  ""btn.delete"": ""Delete"",
  ""btn.check_updates"": ""Check Updates"",
  ""btn.check_deps"": ""Check Dependencies"",
  ""lbl.version"": ""Version"",
  ""lbl.memory"": ""Memory"",
  ""lbl.username"": ""Username"",
  ""lbl.java_path"": ""Java Path"",
  ""lbl.game_dir"": ""Game Directory"",
  ""lbl.install_type"": ""Install Type"",
  ""lbl.vanilla"": ""Vanilla"",
  ""lbl.fabric"": ""Fabric"",
  ""lbl.forge"": ""Forge"",
  ""lbl.modpack"": ""Modpack"",
  ""lbl.modrinth_pack"": ""Modrinth Modpack"",
  ""lbl.account_type"": ""Account Type"",
  ""lbl.offline"": ""Offline"",
  ""lbl.microsoft"": ""Microsoft"",
  ""lbl.authlib"": ""Authlib-Injector"",
  ""lbl.theme"": ""Theme"",
  ""lbl.language"": ""Language"",
  ""lbl.light"": ""Light"",
  ""lbl.dark"": ""Dark"",
  ""lbl.chinese"": ""简体中文"",
  ""lbl.english"": ""English"",
  ""lbl.no_mods"": ""No mods installed"",
  ""lbl.no_deps_issues"": ""No dependency issues detected"",
  ""lbl.deps_ok"": ""All dependencies satisfied"",
  ""lbl.missing_deps"": ""Missing Dependencies"",
  ""lbl.conflict_deps"": ""Conflicting Mods"",
  ""lbl.required"": ""Required"",
  ""lbl.optional"": ""Optional"",
  ""lbl.search_mods"": ""Search mods, shaders, resource packs..."",
  ""lbl.crash_analysis"": ""Crash Analysis"",
  ""lbl.no_crash"": ""No crash report detected"",
  ""msg.installing"": ""Installing {0}..."",
  ""msg.install_done"": ""{0} installed successfully"",
  ""msg.install_failed"": ""{0} installation failed"",
  ""msg.downloading"": ""Downloading ({0}/{1})..."",
  ""msg.launching"": ""Launching {0}..."",
  ""msg.crashed"": ""Game crashed: {0}"",
  ""msg.normal_exit"": ""Game exited normally"",
  ""msg.dep_missing"": ""Missing dependency: {0} ({1})"",
  ""msg.dep_conflict"": ""Conflict: {0} (installed {1}, conflict range {2})"",
  ""msg.skin_fetch_failed"": ""Failed to fetch skin"",
  ""msg.ms_login_hint"": ""Open {0} in browser and enter code {1}"",
  ""msg.authlib_login_failed"": ""Authlib-Injector login failed"",
  ""crash.policy"": ""Crash auto-repair"",
  ""crash.policy.always"": ""Always on"",
  ""crash.policy.ask"": ""Ask each time"",
  ""crash.policy.never"": ""Always off"",
  ""crash.repairable"": ""Auto-repairable issue detected"",
  ""crash.not_repairable"": ""Cannot be auto-repaired (manual fix needed)"",
  ""crash.btn_repair"": ""Try auto-repair"",
  ""crash.repairing"": ""Trying to auto-repair…"",
  ""crash.repaired_success"": ""Repaired and game launched successfully!"",
  ""crash.repaired_recrash"": ""Repair attempted but game crashed again; you may retry."",
  ""crash.repair_unrepairable"": ""Repair attempted but still crashing; cannot auto-repair further."",
  ""crash.repair_failed"": ""Auto-repair failed: {0}"",
  ""crash.non_destructive"": ""All repairs never delete or modify game original files."",
  ""crash.analyzing"": ""Analyzing crash report…"",
  ""crash.open_report"": ""Open crash analysis report"",
  ""tab.game"": ""Game"",
  ""tab.toolbox"": ""Toolbox"",
  ""tab.minecraft"": ""Minecraft"",
  ""tab.shader"": ""Shaders"",
  ""tab.resourcepack"": ""Resource Packs"",
  ""tab.map"": ""Maps"",
  ""tab.install"": ""Install"",
  ""status.java"": ""Java:"",
  ""status.installed"": ""{0} versions installed"",
  ""status.running"": ""{0} instances running"",
  ""status.no_java"": ""No Java detected"",
  ""status.java_low"": ""Java {0} (needs {1}+)"",
  ""status.network_ok"": ""Online"",
  ""status.network_slow"": ""High latency"",
  ""status.network_offline"": ""Offline"",
  ""game.quick_launch"": ""Quick Launch"",
  ""game.lan"": ""LAN Games"",
  ""game.servers"": ""Server List"",
  ""game.recommend"": ""Recommendations"",
  ""game.stats"": ""Statistics"",
  ""game.no_lan"": ""No LAN games found, click refresh to scan"",
  ""game.no_servers"": ""No servers yet, click to add"",
  ""game.add_server"": ""Add Server"",
  ""game.join"": ""Join"",
  ""game.edit"": ""Edit"",
  ""game.password_protected"": ""Password required"",
  ""game.players"": ""{0} players"",
  ""game.latency"": ""Latency"",
  ""game.ping_good"": ""Good"",
  ""game.ping_ok"": ""Fair"",
  ""game.ping_bad"": ""Poor"",
  ""game.install_mod"": ""Install"",
  ""game.not_interested"": ""Not Interested"",
  ""game.dep_missing_red"": ""Missing dependencies (marked in red)"",
  ""game.recent_version"": ""Recent Version"",
  ""game.weekly_time"": ""Weekly Playtime"",
  ""game.crash_count"": ""Crashes"",
  ""game.annual_report"": ""Annual Report"",
  ""game.mode_survival"": ""Survival"",
  ""game.mode_creative"": ""Creative"",
  ""game.mode_adventure"": ""Adventure"",
  ""game.mode_spectator"": ""Spectator"",
  ""game.btn_start"": ""Launch"",
  ""game.account"": ""Account"",
  ""game.no_account"": ""(No account, using offline name)"",
  ""game.username"": ""Username"",
  ""game.memory"": ""Memory MB"",
  ""dl.search_hint"": ""Search mods, shaders, resource packs..."",
  ""dl.filter_version"": ""Version Filter"",
  ""dl.filter_loader"": ""Loader Filter"",
  ""dl.queue"": ""Download Queue"",
  ""dl.pause"": ""Pause"",
  ""dl.retry"": ""Retry"",
  ""dl.pause_all"": ""Pause all"",
  ""dl.resume_all"": ""Resume queue"",
  ""dl.speed_limit"": ""Speed limit"",
  ""dl.speed_limit_tip"": ""0 means unlimited; applies to all downloads"",
  ""dl.auto_retry"": ""Auto-retry on failure"",
  ""dl.resume"": ""Resume"",
  ""dl.clear_queue"": ""Clear Queue"",
  ""dl.no_results"": ""No results found"",
  ""dl.install_modpack"": ""Install Modpack"",
  ""dl.browse_modrinth"": ""Browse Modrinth"",
  ""dl.category"": ""Category"",
  ""dl.sort"": ""Sort"",
  ""dl.sort_hot"": ""Hot"",
  ""dl.sort_new"": ""Newest"",
  ""dl.sort_downloads"": ""Downloads"",
  ""dl.map_views"": ""{0} views"",
  ""dl.map_author"": ""Author: {0}"",
  ""dl.detail"": ""Details"",
  ""dl.extra_resources"": ""Extra Resources"",
  ""dl.downloading"": ""Downloading"",
  ""dl.completed"": ""Completed"",
  ""tool.log"": ""Log Manager"",
  ""tool.crash"": ""Crash Analyzer"",
  ""tool.perf"": ""Performance Monitor"",
  ""tool.network"": ""Network Diagnostics"",
  ""tool.filewatch"": ""File Change Detection"",
  ""tool.datapack"": ""Data Pack Conflict Check"",
  ""tool.saves"": ""Save Manager"",
  ""tool.backup"": ""Backup Manager"",
  ""tool.screenshot"": ""Screenshot Manager"",
  ""tool.clean"": ""Redundant File Cleaner"",
  ""tool.modpackio"": ""Modpack Import/Export"",
  ""tool.music"": ""Music Player"",
  ""tool.moddev"": ""Mod Dev Environment"",
  ""tool.packmaker"": ""Resource Pack Creator"",
  ""tool.nbt"": ""NBT Editor"",
  ""tool.command"": ""Command Reference"",
  ""tool.map"": ""Map Installer"",
  ""tool.skin"": ""Skin Editor"",
  ""tool.seasonal"": ""Festival Hub"",
  ""tool.shortcut"": ""Shortcut Generator"",
  ""tool.afk"": ""AFK Workflow"",
  ""tool.aichat"": ""AI Assistant"",
  ""tool.group.diag"": ""Diagnostics"",
  ""tool.group.resource"": ""Resources & Content"",
  ""tool.group.dev"": ""Dev Tools"",
  ""tool.group.other"": ""Other"",
  ""settings.general"": ""General"",
  ""settings.launch"": ""Launch"",
  ""settings.recommend"": ""Recommendations"",
  ""settings.account"": ""Account"",
  ""settings.music"": ""Music"",
  ""music.set_auto_duck"": ""Lower music volume when the game starts"",
  ""music.set_resume"": ""Resume last track on launcher startup"",
  ""music.set_client_section"": ""Local Client Mode"",
  ""music.set_client_section_tip"": ""Audio is played by an external music client; the launcher only recycles it after you switch source and it stays paused. With the master switch off, no external client process is involved at all."",
  ""music.set_client_enabled"": ""Enable local client mode"",
  ""music.set_client_grace"": ""Client grace period"",
  ""music.set_client_grace_tip"": ""While the player is on local folder or API mode and every launcher-launched client is paused, wait this long then end those client processes. Set to 0 to disable auto close."",
  ""music.set_lyric_pin"": ""Lyric pin mode"",
  ""music.set_lyric_pin_tip"": ""Affects how many lines the lyric overlay renders."",
  ""music.set_lyric_pin_current"": ""Current line only"",
  ""music.set_lyric_pin_next"": ""Current + next line"",
  ""music.set_lyric_pin_prev"": ""Previous + current + next"",
  ""music.set_client_lyric"": ""Still fetch lyrics via API in client mode"",
  ""music.set_client_lyric_tip"": ""When on, the overlay keeps showing lyrics in local client mode (matched by song title from the online lyric library); when off, lyrics are hidden."",
  ""music.set_client_app"": ""Client application"",
  ""music.set_client_tip"": ""Note: in local client mode the launcher outputs no audio. After you switch back to local folder or API mode, clients launched by the launcher are ended once the grace period expires."",
  ""music.source_api"": ""Online"",
  ""music.api_search"": ""Search"",
  ""music.api_keyword_tip"": ""Song title, optionally followed by the artist"",
  ""music.api_playlist_id"": ""Playlist ID"",
  ""music.api_playlist_id_tip"": ""Share the playlist in the music client; the number at the end of the link is its ID"",
  ""music.api_import"": ""Import"",
  ""music.api_my_playlists"": ""My playlists"",
  ""music.api_refresh"": ""Refresh"",
  ""music.api_login"": ""Scan to log in"",
  ""music.api_logout"": ""Log out"",
  ""music.api_cancel"": ""Cancel"",
  ""music.api_account"": ""Account"",
  ""music.api_scan_tip"": ""Scan the QR code in your music app and confirm"",
  ""music.api_results"": ""Results"",
  ""music.api_playlists_section"": ""Playlists"",
  ""music.set_api_section"": ""Online music source"",
  ""music.set_api_kind_vendor"": ""Vendor-hosted API (QR login)"",
  ""music.set_api_section_tip"": ""The launcher streams audio itself over HTTP. The default points to a public Meting instance; use the Netease API with your own service address to get your own playlists."",
  ""music.set_api_enabled"": ""Enable online music source"",
  ""music.set_api_kind"": ""Provider"",
  ""music.set_api_kind_meting"": ""Meting aggregate (no login)"",
  ""music.set_api_kind_netease"": ""Netease API (QR login)"",
  ""music.set_api_baseurl"": ""Service address"",
  ""music.set_api_baseurl_tip"": ""No trailing slash. Switching provider resets it to the matching default."",
  ""music.set_api_platform_kuwo"": ""Kuwo Music"",
  ""music.set_api_platform_baidu"": ""Baidu Music"",
  ""music.set_api_platform_xiami"": ""Xiami Music"",
  ""music.set_api_platform_all"": ""All platforms (aggregate)"",
  ""music.set_api_default"": ""Restore default"",
  ""music.set_api_platform"": ""Platform"",
  ""music.set_api_platform_netease"": ""Netease Cloud Music"",
  ""music.set_api_platform_tencent"": ""QQ Music"",
  ""music.set_api_platform_kugou"": ""Kugou Music"",
  ""music.set_api_quality"": ""Quality"",
  ""music.set_api_quality_standard"": ""Standard 128k"",
  ""music.set_api_quality_higher"": ""Higher 192k"",
  ""music.set_api_quality_exquisite"": ""Exquisite 320k"",
  ""music.set_api_quality_lossless"": ""Lossless"",
  ""music.set_api_account"": ""Sign-in status"",
  ""music.set_api_login_unsupported"": ""This provider does not support QR login"",
  ""settings.ai"": ""AI"",
  ""settings.appearance"": ""Appearance"",
  ""settings.about"": ""About"",
  ""settings.lang"": ""Language"",
  ""settings.download"": ""Download"",
  ""settings.autostart"": ""Auto-start on boot"",
  ""settings.minimize_tray"": ""Minimize to tray"",
  ""settings.animations"": ""Animations"",
  ""settings.toast_duration"": ""Toast duration (seconds)"",
  ""settings.filewatch"": ""File change detection"",
  ""settings.default_memory"": ""Default Memory (MB)"",
  ""settings.default_username"": ""Default Username"",
  ""settings.java_path"": ""Java Path"",
  ""settings.extra_jvm"": ""Extra JVM Args"",
  ""settings.repair_policy"": ""Repair Policy"",
  ""settings.repair_always"": ""Always auto-repair"",
  ""settings.repair_ask"": ""Ask each time"",
  ""settings.repair_never"": ""Never repair"",
  ""settings.java_vendor"": ""Java Vendor"",
  ""settings.java_auto"": ""Auto-detect"",
  ""settings.prewarm"": ""Launch pre-warm"",
  ""settings.hud"": ""HUD overlay"",
  ""settings.launch_compat"": ""Pre-launch compatibility check"",
  ""settings.dl_mirror"": ""Mirror Strategy"",
  ""settings.dl_mirror_auto"": ""Auto"",
  ""settings.dl_mirror_bmcl"": ""BMCLAPI First"",
  ""settings.dl_mirror_official"": ""Official Only"",
  ""settings.dl_concurrent"": ""Max Concurrent Downloads"",
  ""settings.auto_deps"": ""Auto-install missing dependencies"",
  ""settings.auto_deps_always"": ""Always"",
  ""settings.auto_deps_ask"": ""Ask"",
  ""settings.auto_deps_off"": ""Off"",
  ""settings.repair_rp"": ""Auto-repair resource packs"",
  ""settings.server_pack_cache"": ""Server resource pack cache"",
  ""settings.recommend_mode"": ""Recommendation Mode"",
  ""settings.recommend_on"": ""Enabled (local + online)"",
  ""settings.recommend_local"": ""Local rules only"",
  ""settings.recommend_off"": ""Disabled"",
  ""settings.gameplay_prefs"": ""Gameplay Preferences"",
  ""settings.add_offline"": ""Add Offline Account"",
  ""settings.offline_name"": ""Player Name"",
  ""settings.login_ms"": ""Microsoft Login"",
  ""settings.add_authlib"": ""Add Authlib Account"",
  ""settings.authlib_url"": ""Server URL"",
  ""settings.authlib_email"": ""Email"",
  ""settings.authlib_password"": ""Password"",
  ""settings.ai_enable"": ""Enable AI Assistant"",
  ""settings.ai_mode"": ""Deployment"",
  ""settings.ai_external"": ""External API"",
  ""settings.ai_local"": ""Local Ollama"",
  ""settings.ai_endpoint"": ""API Endpoint"",
  ""settings.ai_key"": ""API Key"",
  ""settings.ai_model"": ""Model"",
  ""settings.ai_test"": ""Test Connection"",
  ""settings.ai_status_ok"": ""Service OK"",
  ""settings.ai_status_warn"": ""Slow response"",
  ""settings.ai_status_err"": ""Unreachable"",
  ""settings.ai_crash"": ""Crash interpretation"",
  ""settings.ai_recommend"": ""Recommendation reasoning"",
  ""settings.ai_translate"": ""Mod translation"",
  ""settings.theme_color"": ""Theme Color"",
  ""settings.pick_color"": ""Pick…"",
  ""settings.background"": ""Background Image"",
  ""settings.font_scale"": ""Font Size"",
  ""settings.tab_colors"": ""Index Tab Colors"",
  ""settings.tab_game"": ""Game Tab"",
  ""settings.tab_download"": ""Download Tab"",
  ""settings.tab_toolbox"": ""Toolbox Tab"",
  ""settings.tab_settings"": ""Settings Tab"",
  ""settings.high_dpi"": ""High-DPI screen (enable 2x icons)"",
  ""about.version"": ""Version"",
  ""about.check_update"": ""Check for Updates"",
  ""about.license"": ""Open Source License"",
  ""about.github"": ""GitHub Repository"",
  ""btn.ok"": ""OK"",
  ""btn.yes"": ""Yes"",
  ""btn.no"": ""No"",
  ""btn.close"": ""Close"",
  ""btn.add"": ""Add"",
  ""btn.edit"": ""Edit"",
  ""btn.remove"": ""Remove"",
  ""btn.back"": ""Back"",
  ""btn.next"": ""Next"",
  ""btn.finish"": ""Finish"",
  ""btn.export"": ""Export"",
  ""btn.import"": ""Import"",
  ""btn.apply"": ""Apply"",
  ""btn.reset"": ""Reset"",
  ""btn.copy"": ""Copy"",
  ""btn.paste"": ""Paste"",
  ""btn.browse"": ""Browse..."",
  ""btn.clear"": ""Clear"",
  ""color.lightness"": ""Lightness"",
  ""color.palette"": ""Full Palette"",
  ""color.plane"": ""Gamut (X: Hue · Y: Saturation)"",
  ""msg.loading"": ""Loading..."",
  ""msg.no_data"": ""No data available"",
  ""msg.success"": ""Success"",
  ""msg.failed"": ""Failed"",
  ""msg.confirm_delete"": ""Confirm delete? This cannot be undone."",
  ""msg.saving"": ""Saving..."",
  ""msg.saved"": ""Saved"",
  ""msg.connected"": ""Connected"",
  ""msg.disconnected"": ""Disconnected"",
  ""msg.connecting"": ""Connecting..."",
  ""status.ready"": ""Ready"",
  ""home.game.desc"": ""Launcher home (version select / launch game / instance management)."",
  ""theme.editor.title"": ""Four-color theme (Core.TabThemeConfig)"",
  ""theme.editor.hint"": ""Changes apply to title bar and sidebar selection color instantly; invalid values (not #RRGGBB) fall back to Core defaults."",
  ""java.title"": ""Java Environment"",
  ""java.detect"": ""Detect Local Java"",
  ""java.scanning"": ""Scanning Java (JAVA_HOME / /usr/lib/jvm / /opt/java / PATH)..."",
  ""java.detected"": ""Detected {0} Java installation(s)"",
  ""tool.log.desc"": ""View / search / filter / export game logs and crash reports."",
  ""tool.clean.desc"": ""Scan and clean redundant and leftover files in the game directory."",
  ""tool.backup.desc"": ""Manage manual / scheduled backups of saves and configs."",
  ""tool.screenshot.desc"": ""Browse, delete and package game screenshots."",
  ""tool.crash.desc"": ""Analyze crash reports to locate exception types and fixes."",
  ""tool.datapack.desc"": ""Scan datapack conflicts and format issues with advice."",
  ""tool.saves.desc"": ""Manage saves: compatibility / corruption checks, downgrade and backup."",
  ""tool.skin.desc"": ""Preview and edit player skins; validate size and model."",
  ""tab.minecraft.desc"": ""Download and install Minecraft game versions."",
  ""tab.mods.desc"": ""Browse, install and manage mods; handle dependencies and conflicts."",
  ""tab.shader.desc"": ""Install, preview and configure shader packs."",
  ""tab.resourcepack.desc"": ""Install, preview and manage resource packs."",
  ""tab.modpack.desc"": ""Import, install and upgrade modpacks."",
  ""tab.map.desc"": ""Download and import world map saves."",
  ""tool.perf.desc"": ""Real-time performance monitoring and FPS / TPS analysis."",
  ""tool.network.desc"": ""Diagnose network connections and login servers."",
  ""tool.filewatch.desc"": ""Monitor game directory file changes."",
  ""tool.modpackio.desc"": ""Import and export modpacks."",
  ""tool.music.desc"": ""Manage background music and audio tracks."",
  ""tool.moddev.desc"": ""Mod development helpers (templates / debugging)."",
  ""tool.packmaker.desc"": ""Create and package modpacks."",
  ""tool.nbt.desc"": ""NBT data structure editor."",
  ""tool.command.desc"": ""Generate complex commands and functions."",
  ""tool.shortcut.desc"": ""Generate desktop / start menu shortcuts."",
  ""tool.afk.desc"": ""AFK and lightweight automation."",
  ""tool.aichat.desc"": ""Chat assistant backed by Core.Ai."",
  ""settings.general.desc"": ""General launcher behavior and appearance."",
  ""settings.launch.desc"": ""Game launch arguments, Java and memory settings."",
  ""settings.download.desc"": ""Download sources, concurrency and cache settings."",
  ""settings.recommend.desc"": ""Recommended content and personalization."",
  ""settings.account.desc"": ""Account login and multi-account management."",
  ""settings.ai.desc"": ""AI assistant configuration."",
  ""settings.appearance.desc"": ""Theme and four-color customization."",
  ""settings.about.desc"": ""About Chert Launcher and version info."",
  ""tool.versionlist"": ""Version List"",
  ""tool.versionlist.desc"": ""Manage installed game versions, pick and launch."",
  ""tool.devtools"": ""Dev Tools"",
  ""tool.devtools.desc"": ""Mod scaffold generator, resource pack creator and command cheat-sheet."",
  ""version.settings.title"": ""Version Settings"",
  ""version.display_name"": ""Display Name"",
  ""version.isolation"": ""Isolation Mode"",
  ""isolation.shared"": ""Shared"",
  ""isolation.auto"": ""Auto Isolation"",
  ""isolation.custom"": ""Custom Directory"",
  ""version.effective_dir"": ""Effective Game Directory"",
  ""version.mod_loader"": ""Mod Loader"",
  ""version.install_loader"": ""Install Loader"",
  ""version.lock"": ""Version Lock"",
  ""version.lock.desc"": ""Locked versions are protected from auto-update."",
  ""version.resolution"": ""Resolution & Window"",
  ""version.fullscreen"": ""Launch in Fullscreen"",
  ""version.java"": ""Java & Performance"",
  ""version.max_memory"": ""Max Memory"",
  ""version.min_memory"": ""Min Memory"",
  ""version.extra_jvm"": ""Extra JVM Args"",
  ""mods.manage"": ""Mods / Resource Packs"",
  ""mods.installed"": ""Installed"",
  ""mods.add"": ""Add"",
  ""mods.remove"": ""Remove"",
  ""mods.refresh"": ""Refresh"",
  ""mods.check_update"": ""Check Updates"",
  ""mods.search"": ""Search"",
  ""mods.open_folder"": ""Open Folder"",
  ""resourcepack"": ""Resource Pack"",
  ""shader"": ""Shader"",
  ""install.fabric"": ""Install Fabric"",
  ""install.forge"": ""Install Forge"",
  ""install.neoforge"": ""Install NeoForge"",
  ""install.quilt"": ""Install Quilt"",
  ""crash.view.title"": ""Crash Analysis Report"",
  ""crash.keep_mod_hint"": ""Select the mod to keep (others will be renamed to .disabled):"",
  ""crash.missing_dep_hint"": ""Missing dependencies (will be downloaded and installed automatically):"",
  ""crash.downgrade_cause"": ""Crash likely caused by save downgrade"",
  ""crash.revert_backup"": ""Revert to Backup"",
  ""crash.retry_other"": ""Try Another Method"",
  ""crash.install_original"": ""Install Original Save Version"",
  ""crash.downgrade_note"": ""None of these operations delete the original save: reverting saves the current save separately, and the original is always kept in the backup folder."",
  ""crash.causes_label"": ""Possible causes:"",
  ""crash.suggestions_label"": ""Suggested fixes:"",
  ""crash.tab.causes"": ""Causes & Suggestions"",
  ""crash.tab.full"": ""Full Crash Report"",
  ""savecompat.title"": ""Save Version Incompatible"",
  ""savecompat.detected"": ""The following saves were created by a newer version than the one you are about to launch:"",
  ""savecompat.downgrade"": ""Downgrade These Saves"",
  ""savecompat.install_version"": ""Install Matching Version"",
  ""savecompat.ignore"": ""Launch Anyway (Ignore)"",
  ""update.found"": ""Update Available"",
  ""update.emergency"": ""Emergency update: this version includes important fixes or security updates; we recommend installing it now"",
  ""update.later"": ""Later"",
  ""update.download"": ""Download Update"",
  ""update.download_all"": ""Update All"",
  ""update.gui_only"": ""Update GUI Only"",
  ""update.gui_only_tip"": ""Update the launcher UI only, without downloading the command-line tool (smaller download)"",
  ""update.fetching_cli"": ""Fetching command-line tool…"",
  ""update.downloading_cli_pct"": ""Downloading command-line tool {0}%…"",
  ""update.preparing"": ""Preparing download…"",
  ""lbl.traditional"": ""Traditional Chinese"",
  ""update.version_line"": ""Current {0} → Latest {1}"",
  ""update.emgent_note"": ""(emergency update, please install ASAP)"",
  ""update.recommend_note"": ""(recommended, please update)"",
  ""update.fetching"": ""Fetching update package via built-in downloader…"",
  ""update.applying"": ""Download complete, applying update…"",
  ""update.cancelled"": ""Update cancelled"",
  ""update.failed"": ""Update failed: {0}"",
  ""update.downloading_pct"": ""Downloading… {0}%"",
  ""update.no_changelog"": ""(changelog unavailable; click Download Update to view it on the releases page)"",
  ""settings.music_autoduck"": ""Auto-duck music when game launches"",
  ""settings.music_resume"": ""Resume last music when launcher starts"",
  ""settings.default_isolation"": ""Default isolation for new versions:"",
  ""settings.isolation_shared_label"": ""Shared (same .minecraft)"",
  ""settings.isolation_auto_label"": ""Isolated (own versions/<id>)"",
  ""settings.game_dir_label"": ""Game directory:"",
  ""settings.game_dir_tip"": ""Minecraft game directory (.minecraft). Leave empty for the system default."",
  ""settings.memory_mb"": ""Memory (MB):"",
  ""settings.game_volume"": ""Game volume ducking"",
  ""settings.game_volume_tip"": ""When enabled, lowers the volume by the given percent of its current value on every launch (options.txt is left untouched)"",
  ""settings.offline_username"": ""Offline username:"",
  ""settings.jvm_args"": ""JVM arguments:"",
  ""settings.crash_autorepair_label"": ""Crash auto-repair:"",
  ""settings.java_source"": ""Java source:"",
  ""settings.java_temurin"": ""Temurin"",
  ""settings.java_oracle"": ""Oracle"",
  ""settings.missing_dep_label"": ""Missing dependencies:"",
  ""settings.on_label"": ""On"",
  ""settings.hud_label"": ""HUD overlay"",
  ""settings.global_hotkeys"": ""Global hotkeys"",
  ""settings.global_hotkeys_tip"": ""Ctrl+Alt+Enter launches the last played version; Ctrl+Alt+M shows or hides the launcher (system-wide, works even when unfocused)"",
  ""settings.hud_config"": ""HUD settings"",
  ""settings.hud_fields"": ""Displayed items"",
  ""settings.hud_anchor"": ""Dock position"",
  ""settings.hud_anchor_tl"": ""Top left"",
  ""settings.hud_anchor_tr"": ""Top right"",
  ""settings.hud_anchor_bl"": ""Bottom left"",
  ""settings.hud_anchor_br"": ""Bottom right"",
  ""settings.hud_font_size"": ""Font size"",
  ""settings.hud_opacity"": ""Opacity"",
  ""settings.hud_refresh"": ""Refresh interval"",
  ""settings.hud_margin"": ""Screen margin"",
  ""settings.hud_click_through"": ""Mouse click-through"",
  ""settings.hud_foreground_only"": ""Only when game is focused"",
  ""settings.hud_background"": ""Translucent background"",
  ""settings.hud_reset"": ""Restore defaults"",
  ""settings.hud_tip"": ""Tip: drag the in-game HUD to place it anywhere; it is then remembered as a custom position. With click-through enabled the HUD never blocks mouse input."",
  ""settings.hud_field_fps"": ""FPS"",
  ""settings.hud_field_memory"": ""Memory"",
  ""settings.hud_field_cpu"": ""CPU"",
  ""settings.hud_field_ping"": ""Latency"",
  ""settings.hud_field_coords"": ""Coordinates"",
  ""settings.hud_field_biome"": ""Biome"",
  ""settings.hud_field_gametime"": ""In-game time"",
  ""settings.hud_field_session"": ""Session time"",
  ""settings.launch_compat_label"": ""Pre-launch save compatibility check"",
  ""settings.ms_login_title"": ""Microsoft sign-in"",
  ""settings.ms_code_copied"": ""Device code {0} copied to clipboard — just paste it in the browser"",
  ""settings.download_source"": ""Download source priority:"",
  ""settings.cf_section"": ""CurseForge"",
  ""settings.cf_key"": ""API Key"",
  ""settings.cf_key_hint"": ""Leave blank to use the launcher built-in key (injected at build time); the --curseforge-key argument also overrides it"",
  ""settings.cf_root"": ""API Root"",
  ""settings.cf_root_hint"": ""Leave blank for the official https://api.curseforge.com; a third-party mirror is allowed"",
  ""settings.cf_test"": ""Test connection"",
  ""settings.cf_tip"": ""CurseForge powers the online modpack source and Mod fingerprint matching. Projects whose author disabled third-party distribution cannot be downloaded automatically; the launcher will point you to the website."",
  ""settings.cf_status_disabled"": ""Disabled"",
  ""settings.cf_status_user"": ""Using custom key"",
  ""settings.cf_status_builtin"": ""Using built-in key"",
  ""settings.cf_status_none"": ""No key configured; CurseForge source unavailable"",
  ""settings.cf_testing"": ""Testing CurseForge connection..."",
  ""settings.cf_test_ok"": ""CurseForge connection OK"",
  ""settings.cf_test_fail"": ""CurseForge connection failed: {0}"",
  ""settings.cf_test_unknown"": ""unknown reason"",
  ""settings.mirror_first"": ""Mirror first"",
  ""settings.official_first"": ""Official first"",
  ""settings.max_concurrent"": ""Max concurrent downloads:"",
  ""settings.autorepair_rp_label"": ""Auto-repair resource packs on server join"",
  ""settings.recommend_master"": ""Recommendations master:"",
  ""settings.recommend_enable_label"": ""Enabled"",
  ""settings.account_mgmt"": ""Account Management"",
  ""settings.ms_client_id"": ""Microsoft client_id:"",
  ""settings.ms_client_id_tip"": ""Your Azure app client_id (optional); leave empty to use the built-in default. Device code flow needs no redirect URI."",
  ""settings.ms_devicecode_desc"": ""Microsoft sign-in uses the device code flow, which needs no redirect URI and offers the best compatibility. Leave empty to use the built-in default client_id; to use your own Azure app, fill in its client_id. After saving, click 'Login Microsoft' and enter the device code in the browser as prompted."",
  ""settings.set_current"": ""Set as current"",
  ""settings.authlib_title"": ""Authlib-Injector Account"",
  ""settings.authlib_desc"": ""External auth: enter a third-party Authlib-Injector server URL and log in with that system's account (email + password) to bypass official Microsoft verification. Use only on private/community servers you trust. Microsoft login failures are usually due to an unconfigured or rejected Azure client_id; check your credentials."",
  ""settings.authlib_hint1"": ""1. Server URL: the third-party Authlib-Injector URL, e.g. https://auth.example.com"",
  ""settings.authlib_hint2"": ""2. Email: the account email for that server"",
  ""settings.authlib_hint3"": ""3. Password: the corresponding email password"",
  ""settings.bg_image"": ""Background image:"",
  ""settings.show_borders"": ""Show control borders"",
  ""settings.follow_system_theme"": ""Follow system theme"",
  ""settings.follow_system_theme_tip"": ""Switch launcher light/dark automatically with the Windows system theme"",
  ""settings.ui_style"": ""Interface style"",
  ""settings.ui_style_tip"": ""Switch the overall look; Android style moves main navigation to the bottom"",
  ""settings.ui_style_standard"": ""Standard (color tabs)"",
  ""settings.ui_style_android"": ""Minimal Android"",
  ""settings.ui_style_glass"": ""Minimal frosted glass"",
  ""settings.ui_style_dynamic"": ""Dynamic"",
  ""settings.ui_style_switched"": ""Switched to"",
  ""settings.ui_style_pick"": ""Select"",
  ""settings.ui_style_inuse"": ""In use"",
  ""settings.window_bg_color"": ""Window background"",
  ""settings.glass_blur"": ""Frosted glass effect"",
  ""settings.glass_opacity"": ""Glass opacity"",
  ""settings.hide_sidebar_labels"": ""Hide sidebar item labels"",
  ""settings.touch_mode"": ""Touch mode"",
  ""settings.touch_mode_tip"": ""Show a virtual keypad with the game (direction keys and jump key by default)"",
  ""settings.touch_config"": ""Virtual keypad layout"",
  ""settings.touch_edit"": ""Edit layout"",
  ""settings.touch_reset"": ""Restore defaults"",
  ""settings.touch_tip"": ""Drag the round buttons to reposition them; add or remove keys, change key code and size. The panel appears with the game and closes when it exits."",
  ""settings.autoupdate_check"": ""Check for updates on startup"",
  ""settings.license_github"": ""Open Source License · GitHub Repository"",
  ""settings.reset_default"": ""Reset to default"",
  ""settings.auto_detect"": ""Auto-detect"",
  ""settings.open_label"": ""Open"",
  ""ai.m01"": ""Chert AI Assistant"",
  ""ai.m02"": ""I can analyze crash logs, recommend mods, translate descriptions, generate annual summaries, and chat with you."",
  ""ai.m03"": ""Auto-analyze the latest crash log"",
  ""ai.m04"": ""Translate English mod descriptions to Chinese"",
  ""ai.m05"": ""Loadout Recommendations"",
  ""ai.m06"": ""Recommend mods by your playstyle"",
  ""ai.m07"": ""Annual Summary"",
  ""ai.m08"": ""Summarize your play records this year"",
  ""ai.m09"": ""Type your question… (crash logs can be pasted)"",
  ""ai.m10"": ""Send"",
  ""ai.m11"": ""When enabled, choose local deployment or external API for crash analysis, mod recommendation/translation, etc."",
  ""ai.m12"": ""External API (enter key to use; no download, no footprint)"",
  ""ai.m13"": ""Local deployment (auto-install Ollama + lightweight model)"",
  ""ai.m14"": ""Local deployment (Ollama)"",
  ""ai.m15"": ""One-click install Ollama"",
  ""ai.m16"": ""Service status:"",
  ""ai.m17"": ""External API (OpenAI compatible)"",
  ""ai.m18"": ""API Endpoint"",
  ""ai.m19"": ""Model Name"",
  ""ai.m20"": ""AI Features"",
  ""ai.m21"": ""Smart crash log interpretation"",
  ""ai.m22"": ""Generate recommendation reasons"",
  ""ai.m23"": ""Mod description translation"",
  ""ai.m24"": ""Clear chat"",
  ""annual.m24"": ""Year"",
  ""annual.m25"": ""Annual title:"",
  ""annual.m26"": ""Copy Token"",
  ""annual.m27"": ""Export Markdown"",
  ""annual.m28"": ""Total hours"",
  ""annual.m29"": ""Launch count"",
  ""annual.m30"": ""Active days"",
  ""annual.m31"": ""Longest streak (days)"",
  ""annual.m32"": ""Longest single session"",
  ""annual.m33"": ""Monthly distribution (hours)"",
  ""annual.m34"": ""Version ranking (hours)"",
  ""annual.m35"": ""One-line insight"",
  ""annual.m36"": ""Share token (fully offline: copy and send to a friend; they paste to view your annual report)"",
  ""annual.m37"": ""Paste your friend's share token here"",
  ""annual.m38"": ""Friend's"",
  ""annual.m39"": ""Annual report (title: "",
  ""annual.m40"": ""Total time "",
  ""annual.m41"": "" h · "",
  ""annual.m42"": "" launches · active "",
  ""annual.m43"": "" days · crashes "",
  ""annual.m44"": "" times"",
  ""backup.m45"": ""Backup Policy"",
  ""backup.m46"": ""Storage Path"",
  ""backup.m47"": ""Relative paths resolve against the game directory; absolute paths back up to an external drive"",
  ""backup.m48"": ""Browse…"",
  ""backup.m49"": ""Open Folder"",
  ""backup.m50"": ""Scheduled Backup"",
  ""backup.m51"": ""Keep per source"",
  ""backup.m52"": ""Auto-delete oldest when over limit (0 = unlimited)"",
  ""backup.m53"": ""copies"",
  ""backup.m54"": ""Auto-backup retention"",
  ""backup.m55"": ""Auto-backups older than N days are purged (0 = keep by time off); manual backups unaffected"",
  ""backup.m56"": ""days"",
  ""backup.m57"": ""Auto-backup current state before restore"",
  ""backup.m58"": ""Auto-backup saves before launching"",
  ""backup.m59"": ""Save Policy"",
  ""backup.m60"": ""Backup Source"",
  ""backup.m61"": ""Note"",
  ""backup.m62"": ""Optional; helps identify this backup later"",
  ""backup.m63"": ""Backup Now"",
  ""backup.m64"": ""Source"",
  ""backup.m65"": ""Type"",
  ""backup.m66"": ""Created"",
  ""backup.m67"": ""Size"",
  ""backup.m68"": ""Files"",
  ""backup.m69"": ""Restore Selected"",
  ""backup.m70"": ""Delete Selected"",
  ""backup.m71"": ""Clean by Policy"",
  ""cmd.m72"": ""Common Syntax"",
  ""cmd.m73"": ""Insert"",
  ""cmd.m74"": ""Command Builder / Editor"",
  ""crash.m75"": ""Latest Crash Report"",
  ""crash.m76"": ""Analysis & Suggestions"",
  ""crash.m77"": ""Open Full Report"",
  ""datapack.m78"": ""Save"",
  ""datapack.m79"": ""Target Version"",
  ""datapack.m80"": ""e.g. 1.21; used for pack_format warning; leave blank to skip"",
  ""datapack.m81"": ""Scan"",
  ""datapack.m82"": ""Refresh Saves"",
  ""datapack.m83"": ""Update Rule Library"",
  ""datapack.m84"": ""Restore Built-in"",
  ""datapack.m85"": ""Active:"",
  ""datapack.m86"": ""  Overridden:"",
  ""datapack.m87"": ""Jump to Data Pack"",
  ""datapack.m88"": ""Data Packs (load order)"",
  ""datapack.m89"": ""  Missing pack.mcmeta"",
  ""datapack.m90"": ""Format Warning"",
  ""datapack.m91"": ""Suggestions:"",
  ""dev.m92"": ""Command Cheat Sheet"",
  ""dev.m93"": ""Mod Scaffold"",
  ""dev.m94"": ""Resource Pack Creator"",
  ""dev.m95"": ""Mod Name"",
  ""dev.m96"": ""Version"",
  ""dev.m97"": ""Loader"",
  ""dev.m98"": ""Game Version"",
  ""dev.m99"": ""Package name (e.g. com.example.mymod)"",
  ""dev.m100"": ""Generate Project Scaffold"",
  ""dev.m101"": ""Beginner Tutorial"",
  ""dev.m102"": ""1. Enter name and pack_format (1.21=34, 1.20=15, 1.19=9)"",
  ""dev.m103"": ""2. After generating, put textures/models/sounds under assets/minecraft"",
  ""dev.m104"": ""3. Zip it or drop it into .minecraft/resourcepacks"",
  ""dev.m105"": ""Resource Pack Name"",
  ""dev.m106"": ""pack_format (format number for the MC version)"",
  ""dev.m107"": ""Generate Resource Pack"",
  ""dl.m108"": ""Download Center (Modrinth)"",
  ""dl.m109"": ""Search Keyword"",
  ""dl.m110"": ""Add to Queue"",
  ""dl.m111"": ""Start Download Queue"",
  ""dl.m112"": ""Clear Completed"",
  ""dl.m113"": ""This map bundles resource packs / shaders; install them together from details"",
  ""dl.m114"": ""Has Extra Resources"",
  ""dl.m115"": ""Modpack Source"",
  ""dl.m116"": ""Version Type"",
  ""dl.m117"": ""All"",
  ""dl.m118"": ""Release"",
  ""dl.m119"": ""Snapshot"",
  ""dl.m120"": ""Old"",
  ""dl.m121"": ""Latest"",
  ""dl.m122"": ""Most Viewed"",
  ""dl.m123"": ""Previous"",
  ""dl.m124"": ""Page "",
  ""dl.m125"": "" "",
  ""dl.m126"": ""Next"",
  ""dl.m127"": ""Author"",
  ""dl.m128"": ""Browse"",
  ""dl.m129"": ""Like"",
  ""dl.m130"": ""Rating"",
  ""dl.m131"": ""Map Site Page"",
  ""dl.m132"": ""Download resource packs / shaders and auto-distribute to resourcepacks, shaderpacks"",
  ""dl.m133"": ""Download to Save"",
  ""dl.m134"": ""Select Version"",
  ""dl.m135"": ""Isolated install (separate versions/<name> directory)"",
  ""dl.m136"": ""Source Page"",
  ""dl.m137"": ""Version Count"",
  ""dl.m138"": ""AI Translate"",
  ""dl.m139"": ""Unavailable when AI assistant is disabled"",
  ""dl.m140"": ""Project Page"",
  ""dl.m141"": ""Install Selected Version"",
  ""dl.m142"": ""Install Minecraft Version"",
  ""dl.m143"": ""Select loader (optional)"",
  ""dl.m144"": ""Vanilla (no loader)"",
  ""dl.m145"": ""Fabric (auto-pairs latest Fabric API)"",
  ""game.m146"": ""📦 Version Library"",
  ""game.m147"": ""Open full version list"",
  ""game.m148"": ""No LAN worlds found. Enable 'Open to LAN' in-game then click refresh."",
  ""game.m149"": ""Server list is empty."",
  ""game.m150"": ""Refresh Recommendations"",
  ""game.m151"": ""Dependencies"",
  ""game.m152"": ""No recommendations yet; click 'Refresh Recommendations'."",
  ""game.m153"": ""Crash count (year)"",
  ""game.m154"": ""View"",
  ""game.m155"": ""View your annual play stats today"",
  ""home.m156"": ""Select version:"",
  ""home.m157"": ""Play Stats"",
  ""home.m158"": ""Total Playtime"",
  ""home.m159"": ""Recommended for You"",
  ""home.m160"": ""Dependency Completion"",
  ""install.m161"": ""Install New Version"",
  ""install.m162"": ""Type:"",
  ""install.m163"": ""Version:"",
  ""log.m164"": ""Errors only"",
  ""map.m165"": ""Install Map"",
  ""moddev.m166"": ""Display Name"",
  ""moddev.m167"": ""Target MC Version"",
  ""moddev.m168"": ""Target Directory"",
  ""moddev.m169"": ""Generate Mod Scaffold"",
  ""modpack.m170"": ""Export Modpack"",
  ""modpack.m171"": ""Display Name (optional)"",
  ""modpack.m172"": ""Includes Mods"",
  ""modpack.m173"": ""Includes Config"",
  ""modpack.m174"": ""Includes Resource Packs"",
  ""modpack.m175"": ""Includes Shader Packs"",
  ""modpack.m176"": ""Includes Saves"",
  ""modpack.m177"": ""Export…"",
  ""modpack.m178"": ""Import Modpack"",
  ""modpack.m179"": ""Supports Modrinth .mrpack modpacks."",
  ""modpack.m180"": ""Choose a file and import…"",
  ""music.m181"": ""Audio Source:"",
  ""music.m182"": ""Local Folder"",
  ""music.m183"": ""Online Stream"",
  ""music.m184"": ""MC OST"",
  ""music.client_mode"": ""Local Client"",
  ""music.client_title"": ""Local Client Mode"",
  ""music.client_desc"": ""Audio is played by an external client; the launcher no longer outputs sound. After you switch to another source, the client is closed automatically once it stays paused for the configured duration."",
  ""music.client_choose"": ""Choose App"",
  ""music.client_launch"": ""Launch Client"",
  ""music.client_lyric_hint"": ""In client mode lyrics are matched by song title from the online lyric library; nothing shows if the client is playing a different track."",
  ""music.m185"": ""Load Music Folder"",
  ""music.m186"": ""Online stream URL (direct play link)"",
  ""music.m187"": ""Preset:"",
  ""music.m188"": ""Scan MC OST"",
  ""music.m189"": ""Delete Track"",
  ""music.col_name"": ""Name"",
  ""music.col_title"": ""Title"",
  ""music.col_artist"": ""Artist"",
  ""music.col_album"": ""Album"",
  ""music.col_duration"": ""Length"",
  ""nbt.m190"": ""Open…"",
  ""nbt.m191"": ""Save As…"",
  ""nbt.m192"": ""Export Text"",
  ""nbt.m193"": ""Copy the original to .bak before saving"",
  ""nbt.m194"": ""Auto-backup before save"",
  ""nbt.m195"": ""Quick access to save's level.dat"",
  ""nbt.m196"": ""Expand All"",
  ""nbt.m197"": ""Collapse All"",
  ""nbt.m198"": ""Selected Node"",
  ""nbt.m199"": ""Path"",
  ""nbt.m200"": ""Value (scalars only editable)"",
  ""nbt.m201"": ""Apply Changes"",
  ""nbt.m202"": ""Add / Rename"",
  ""nbt.m203"": ""Name"",
  ""nbt.m204"": ""Add Child Tag"",
  ""nbt.m205"": ""Rename"",
  ""nbt.m206"": ""Delete Selected Tag"",
  ""net.m207"": ""Re-detect"",
  ""perf.m208"": ""Running Instances & Performance"",
  ""perf.m209"": ""System CPU Usage"",
  ""perf.m210"": ""Memory Usage"",
  ""perf.m211"": ""Running Instances"",
  ""perf.m212"": ""CPU Logical Cores"",
  ""perf.m213"": ""Available Memory"",
  ""perf.m214"": ""Total playtime (min)"",
  ""perf.m215"": ""Launch Time"",
  ""clean.m216"": ""Clean Selected"",
  ""clean.m217"": ""Delete directly (unrecoverable)"",
  ""saves.m218"": ""Save Manager"",
  ""saves.m219"": ""Target game version:"",
  ""saves.m220"": ""Scan Compatibility"",
  ""saves.m221"": ""Scan Corruption"",
  ""saves.m222"": ""(§2.4 detection / §3 downgrade & rollback / corruption detection is scan-only)"",
  ""saves.m223"": ""Current version:"",
  ""saves.m224"": ""Backup:"",
  ""saves.m225"": ""Corruption check:"",
  ""saves.m226"": ""Extract Seed"",
  ""saves.m227"": ""Backup"",
  ""saves.m228"": ""Downgrade"",
  ""saves.m229"": ""Rollback"",
  ""screenshot.m230"": ""Package & Share"",
  ""server.m231"": ""Capacity Limit"",
  ""server.m232"": ""Oldest evicted by LRU once over limit"",
  ""server.m233"": ""Export to Folder"",
  ""server.m234"": ""Open Cache Folder"",
  ""server.m235"": ""Clear"",
  ""server.m236"": ""Total "",
  ""server.m237"": "" caches, using "",
  ""server.m238"": "" MB (total hits "",
  ""server.m239"": "") capacity limit "",
  ""server.m240"": ""File Name"",
  ""server.m241"": ""Source Server"",
  ""server.m242"": ""Hits"",
  ""server.m243"": ""Last Used"",
  ""server.m244"": ""No server resource packs cached yet. When a server pushes one, it appears here automatically."",
  ""server.m245"": ""Export to Resource Pack Folder"",
  ""shader.m246"": ""Shader Config Params"",
  ""shader.m247"": ""+ Add Param"",
  ""shader.m248"": ""Param ID (e.g. shadowRes)"",
  ""shader.m249"": ""Param value (e.g. 2048)"",
  ""shader.m250"": ""Full Token"",
  ""shader.m251"": ""Import Token"",
  ""shader.m252"": ""Parse from Token"",
  ""shader.m253"": ""Load"",
  ""shortcut.m254"": ""Create on Desktop"",
  ""skin.m255"": ""Body Part"",
  ""skin.m256"": ""Tool"",
  ""skin.m257"": ""Symmetric Draw"",
  ""skin.m258"": ""Eraser"",
  ""skin.m259"": ""Brush Size"",
  ""skin.m260"": ""← Undo"",
  ""skin.m261"": ""Redo →"",
  ""skin.m262"": ""Clear All"",
  ""skin.m263"": ""Import PNG"",
  ""skin.m264"": ""Export PNG"",
  ""skin.m265"": ""Apply to Offline Account"",
  ""skin.m266"": ""2D Editor"",
  ""skin.m267"": ""3D Preview"",
  ""skin.m268"": ""Slim Arms"",
  ""skin.m269"": ""Zoom"",
  ""skin.m270"": ""Fill"",
  ""skin.m271"": ""64x64 Preview"",
  ""skin.m272"": ""Current Color"",
  ""skin.m273"": ""Palette"",
  ""skin.m274"": ""Player name:"",
  ""skin.m275"": ""Query"",
  ""skin.m276"": ""Drag to rotate · scroll to zoom"",
  ""skin.m277"": ""Enter a legitimate Minecraft username to fetch the skin"",
  ""skin.m304"": ""Color Picker (Full Palette)"",
  ""skin.m305"": ""Secondary ← Primary"",
  ""ver.m278"": ""← Back"",
  ""ver.m279"": ""Return to Previous"",
  ""ver.m280"": ""Installed Versions"",
  ""ver.m281"": ""Version Settings…"",
  ""ver.search"": ""Search"",
  ""ver.filter_all"": ""All"",
  ""ver.filter_loader"": ""Loader"",
  ""ver.filter_dir"": ""Directory"",
  ""ver.filter_content"": ""Content"",
  ""ver.filter_missing"": ""Missing only"",
  ""ver.filter_reset"": ""Clear"",
  ""ver.iso"": ""Isolated"",
  ""ver.shared"": ""Shared"",
  ""ver.content_mods"": ""Mods"",
  ""ver.content_rp"": ""Resource Packs"",
  ""ver.content_sp"": ""Shaders"",
  ""ver.content_dp"": ""Data Packs"",
  ""ver.content_saves"": ""Saves"",
  ""ver.loader_quilt"": ""Quilt"",
  ""ver.loader_neoforge"": ""NeoForge"",
  ""version.m282"": ""① Basic Info"",
  ""version.m283"": ""Delete This Version"",
  ""version.m284"": ""Version Id:"",
  ""version.m285"": ""  | Type:"",
  ""version.m286"": ""  | Base Version:"",
  ""version.m287"": ""③ Mod Loader"",
  ""version.m288"": ""Current:"",
  ""version.m289"": ""The loader installs as a new instance (based on the base version above); switch to it in the version list."",
  ""version.m290"": ""④ Isolation & Work Directory"",
  ""version.m291"": ""Effective work directory:"",
  ""version.m292"": ""⑤ Java & Performance"",
  ""version.m293"": ""Extra JVM args (one per line, appended after global args)"",
  ""version.jvm_presets"": ""Common flags (check to add)"",
  ""version.jvm_custom"": ""Custom arguments (one per line)"",
  ""version.jvm_preview"": ""Resulting arguments (read-only)"",
  ""version.jvm_g1gc"": ""Use the G1 garbage collector"",
  ""version.jvm_g1gc_tip"": ""Better pause control for large heaps (recommended on Java 8+)"",
  ""version.jvm_strdedup"": ""String deduplication"",
  ""version.jvm_strdedup_tip"": ""Requires G1; reduces memory used by duplicate strings"",
  ""version.jvm_dis_explicit"": ""Disable explicit GC"",
  ""version.jvm_dis_explicit_tip"": ""Ignores System.gc() calls triggered by mods"",
  ""version.jvm_pretouch"": ""Pre-touch memory at startup"",
  ""version.jvm_pretouch_tip"": ""Slightly slower startup for steadier runtime allocation"",
  ""version.jvm_parref"": ""Parallel reference processing"",
  ""version.jvm_parref_tip"": ""Shortens the reference-processing phase of GC"",
  ""version.jvm_log4j"": ""Disable Log4j2 message lookups"",
  ""version.jvm_log4j_tip"": ""Mitigates the Log4Shell path on older versions"",
  ""version.jvm_fml_cert"": ""Forge: ignore invalid certificates"",
  ""version.jvm_fml_cert_tip"": ""Fixes launch failures caused by Forge certificate checks"",
  ""version.jvm_fml_patch"": ""Forge: ignore patch discrepancies"",
  ""version.jvm_fml_patch_tip"": ""Fixes launch failures from Forge patch verification"",
  ""version.jvm_encoding"": ""Force UTF-8 encoding"",
  ""version.jvm_encoding_tip"": ""Avoids mojibake in non-ASCII paths and chat"",
  ""version.jvm_maxpause"": ""Max GC pause target (ms)"",
  ""version.jvm_maxpause_tip"": ""G1 tries to keep single GC pauses within this value"",
  ""version.jvm_gcthreads"": ""GC parallel threads"",
  ""version.jvm_gcthreads_tip"": ""Usually no more than the CPU core count"",
  ""version.jvm_ihop"": ""Concurrent GC heap occupancy (%)"",
  ""version.jvm_ihop_tip"": ""Starts concurrent marking when heap usage reaches this"",
  ""version.m294"": ""⑥ Resolution & Window"",
  ""version.m295"": ""Width"",
  ""version.m296"": ""Height"",
  ""version.m297"": ""⑦ Mods & Resource Pack Management"",
  ""version.m298"": ""Search Modrinth"",
  ""version.m299"": ""⑧ Version Lock"",
  ""version.m300"": ""Lock this version (block auto-update override and loader install / mod add-remove)"",
  ""version.m301"": ""⑨ Account Binding"",
  ""version.m302"": ""Bind Account"",
  ""version.m303"": ""Clear Binding"",
  ""install.start"": ""Start Install"",
  ""install.cancel"": ""Cancel Install"",
  ""install.progress"": ""Progress"",
  ""install.refresh"": ""Refresh"",
  ""install.done"": ""Installed; version list refreshed"",
  ""install.cancelled"": ""Install cancelled"",
  ""install.failed"": ""Install failed"",
  ""map.zip_hint"": ""Map zip is auto-installed to saves/, with root-directory prefix detection."",
  ""shortcut.title"": ""Version Shortcut"",
  ""shortcut.display_name"": ""Display Name (optional)"",
  ""shortcut.tip"": ""Tip: double-click the shortcut to launch the game with this version (passes --launch <version> to the launcher)."",
  ""ai.ollama_pre"": ""Ollama installed ("",
  ""ai.ollama_post"": "")"",
  ""annual.month"": ""mo"",
  ""dl.total_pre"": ""Total "",
  ""dl.total_post"": "" items"",
  ""home.minutes"": "" min"",
  ""rec.downloads"": "" downloads"",
  ""skin.model_pre"": ""Model: "",
  ""achieve.title"": ""Achievements"",
  ""achieve.purple"": ""Purple achievements: {0}"",
  ""afk.add_action"": ""+ Add Action"",
  ""afk.action_type"": ""Action Type"",
  ""afk.param"": ""Parameter"",
  ""afk.plaintext_tip"": ""Plain text (auto-encoded to base64)"",
  ""afk.run"": ""Run"",
  ""afk.stop"": ""Stop"",
  ""afk.token"": ""Token"",
  ""afk.save_workflow"": ""Save Workflow"",
  ""afk.import_token"": ""Import Token"",
  ""afk.import_from_token"": ""Import from Token"",
  ""afk.saved_workflows"": ""Saved Workflows"",
  ""afk.load"": ""Load"",
  ""afk.delete_workflow_tip"": ""Delete this workflow"",
  ""afk.picker_title"": ""Select Action Type"",
  ""afk.picker_hint"": ""Click an action to add it to the workflow"",
  ""afk.type_f"": ""Function Key"",
  ""afk.type_d"": ""Delay"",
  ""afk.type_l"": ""Long Press"",
  ""afk.type_k"": ""Virtual Key"",
  ""afk.type_c"": ""Left Click"",
  ""afk.type_r"": ""Right Click"",
  ""afk.type_m"": ""Mouse Move"",
  ""afk.type_g"": ""Hold"",
  ""afk.type_j"": ""Random Wait"",
  ""afk.type_s"": ""Scroll"",
  ""afk.type_t"": ""Text"",
  ""afk.type_u"": ""Release"",
  ""afk.type_loop"": ""Loop"",
  ""afk.type_e"": ""Key"",
  ""cmd.copy_clipboard"": ""Copy Command"",
  ""server.name"": ""Server Name"",
  ""server.address"": ""Server Address"",
  ""server.name_empty"": ""Server name cannot be empty"",
  ""server.addr_empty"": ""Server address cannot be empty"",
  ""settings.theme"": ""Theme"",
  ""settings.isolation_tip"": ""Isolation mode auto-applied to new versions: Shared = same .minecraft; Isolated = separate versions/<id>"",
  ""settings.ms_client_id_note"": ""(leave empty to use the built-in default client_id)"",
  ""settings.save_settings"": ""Save Settings"",
  ""nat.label"": ""Network type"",
  ""nat.probing"": ""Measuring…"",
  ""nat.not_probed"": ""Not measured yet"",
  ""nat.eim"": ""Cone NAT — the other side can reach you"",
  ""nat.ad"": ""Address-dependent NAT — direct link often fails"",
  ""nat.apd"": ""Symmetric NAT — direct link is nearly impossible"",
  ""nat.unknown"": ""Could not tell (still worth trying)"",
  ""nat.udp_blocked"": ""UDP seems blocked — no STUN server answered"",
  ""nat.stun"": ""STUN server"",
  ""nat.stun_tip"": ""Leave empty to pick automatically. Campus and office networks often need a specific one: write it as host:3478, separate several with commas"",
  ""nat.fallback_tip"": ""If direct link fails: put both PCs on the same VPN mesh (ZeroTier, Radmin VPN, ...) and then use the LAN link above, which does not care about NAT at all."",
  ""settings.font_scaling"": ""Font scaling:"",
  ""mods.mgmt_title"": ""Mod Management"",
  ""mods.missing_dep_label"": ""Missing dependencies:"",
  ""mods.required_tag"": ""[Required]"",
  ""mods.conflict_label"": ""Conflicts:"",
  ""mods.installed_tag"": "" installed "",
  ""mods.conflict_range_tag"": "" conflict range "",
  ""mods.col_name"": ""Name"",
  ""mods.col_modid"": ""Mod ID"",
  ""mods.col_version"": ""Version"",
  ""mods.col_loader"": ""Loader"",
  ""p2p.title"": ""Direct link over the Internet"",
  ""p2p.section_tip"": ""Use this when the two of you are not on the same LAN: swap a handshake code once, then punch through NAT and talk directly. No server in the middle ever."",
  ""p2p.host_btn"": ""1) I host the world — generate an invite"",
  ""p2p.join_btn"": ""2) I received an invite code"",
  ""p2p.host_btn_tip"": ""Minecraft must already be running and open to LAN (hit 'Read local port' above first)"",
  ""p2p.join_btn_tip"": ""Paste the CHERT2: code your friend sent you"",
  ""p2p.my_ip"": ""My public IP"",
  ""p2p.my_ip_tip"": ""Leave empty if unsure. Needed only when your router refuses UPnP and you have no public IPv6"",
  ""p2p.my_code"": ""My code (send this)"",
  ""p2p.copy_code"": ""Copy"",
  ""p2p.qr_tip"": ""Let them scan this, or just send the text below"",
  ""p2p.fingerprint"": ""Fingerprint"",
  ""p2p.fingerprint_tip"": ""Both sides should see the same digits — otherwise you may be talking to a stranger"",
  ""p2p.peer_code"": ""Their code"",
  ""p2p.peer_code_host_tip"": ""Paste the answer code they replied with"",
  ""p2p.peer_code_join_tip"": ""Paste the host's invite code"",
  ""p2p.connect"": ""Connect"",
  ""p2p.stop"": ""Disconnect"",
  ""p2p.reconnect"": ""Retry"",
  ""p2p.reconnect_tip"": ""Punch again with the codes you already exchanged. Both sides must click — a one-sided retry will just time out."",
  ""p2p.role_host"": ""Host"",
  ""p2p.role_joiner"": ""Guest"",
  ""p2p.ready_joiner"": ""Linked! In Minecraft use Multiplayer → Direct Connect and enter: {0}"",
  ""p2p.ready_host"": ""Tunnel ready, waiting for the other player"",
  ""p2p.ready_hint"": ""Linked! In Minecraft go to Multiplayer → Direct Connect and enter {0}"",
  ""p2p.need_local_port"": ""Hit 'Read local port' above first — the launcher needs to know which port to forward to"",
  ""p2p.need_choice"": ""Pick whether you are hosting or joining first"",
  ""p2p.no_code"": ""No code generated yet"",
  ""p2p.code_copied"": ""Copied — send it to the other player"",
  ""p2p.unsupported"": ""Direct link is unavailable here: QUIC (Windows 11+) and X25519 key exchange are required"",
  ""mods.col_latest"": ""Latest"",
  ""mods.select_label"": ""Select:"",
  ""mods.select_all"": ""Select All"",
  ""mods.select_none"": ""Select None"",
  ""mods.select_outdated"": ""Outdated"",
  ""mods.selected_count"": ""Selected "",
  ""mods.verify_selected"": ""Verify"",
  ""mods.verify_all"": ""Verify All"",
  ""mods.update_selected"": ""Update"",
  ""mods.update_all"": ""Update All"",
  ""mods.update_one"": ""Update"",
  ""mods.enable"": ""Enable"",
  ""mods.disable"": ""Disable"",
  ""mods.toggle"": ""On/Off"",
  ""mods.uninstall_selected"": ""Uninstall"",
  ""mods.col_state"": ""State"",
  ""mods.col_verify"": ""Verification"",
  ""mods.col_action"": ""Actions"",
  ""mods.uninstall"": ""Uninstall"",
  ""seasonal.midautumn.greeting.title"": ""Happy Mid-Autumn Festival"",
  ""seasonal.midautumn.greeting.message"": ""Wishing you a full moon and joyful moments. Seasonal effects can be turned off in Settings."",
  ""seasonal.midautumn.name"": ""Mid-Autumn Festival"",
  ""seasonal.midautumn.splash.title"": ""Happy Mid-Autumn"",
  ""seasonal.midautumn.splash.message"": ""May the moonlight guide every adventure ahead"",
  ""seasonal.hub_title"": ""Festival Hub"",
  ""seasonal.easter_egg"": ""🥚 Easter Egg Game"",
  ""seasonal.easter_egg_tip"": ""Catch the fake block: click it before it slips away. Missed clicks cost points."",
  ""tool.lanlink"": ""LAN Link"",
  ""lan.title"": ""LAN Link"",
  ""lan.enable"": ""Discoverable on LAN"",
  ""lan.enable_tip"": ""Listen on discovery/command ports so another Chert on the same Wi-Fi can find you"",
  ""lan.service_tip"": ""Windows Firewall prompts on first listen - click Allow. No admin rights needed. Turning the switch off stops listening."",
  ""lan.my_world"": ""My world"",
  ""lan.publish"": ""Open to LAN"",
  ""lan.publish_tip"": ""Send /publish to the game window (same as Open to LAN in-game)"",
  ""lan.detect"": ""Read local port"",
  ""lan.detect_tip"": ""Read the published LAN port from the game log"",
  ""lan.copy_share"": ""Copy invite"",
  ""lan.devices"": ""Devices on LAN"",
  ""lan.manual_ip"": ""Peer IP"",
  ""lan.manual_ip_tip"": ""Probe a peer directly when broadcast is blocked"",
  ""lan.probe"": ""Probe"",
  ""lan.pair"": ""Pairing"",
  ""lan.gen_code"": ""Generate code"",
  ""lan.input_code"": ""Peer code"",
  ""lan.do_pair"": ""Pair"",
  ""lan.pair_tip"": ""Each side generates a code and enters the other one. Codes expire in 10 minutes; unpaired devices are always rejected."",
  ""lan.actions"": ""Actions"",
  ""lan.invite"": ""Invite to my world"",
  ""lan.invite_tip"": ""Send your world address; the peer gets a confirmation dialog"",
  ""lan.request_open"": ""Ask peer to open"",
  ""lan.request_open_tip"": ""Ask the peer Minecraft to run /publish"",
  ""lan.join_addr"": ""World address"",
  ""lan.join"": ""Join"",
  ""lan.join_tip"": ""Paste a CHERT1: invite code, or an address like 192.168.1.5:52931"",
  ""lan.invite_title"": ""LAN invite"",
  ""lan.gen_invite"": ""Create code"",
  ""lan.gen_invite_tip"": ""Pack address / device name / pair code into one copyable string"",
  ""lan.copy_invite"": ""Copy code"",
  ""lan.copy_invite_tip"": ""Copy the whole string (with the CHERT1: prefix)"",
  ""lan.copy_code"": ""Copy"",
  ""lan.code_none"": ""No pair code yet - generate one first"",
  ""lan.code_copied"": ""Pair code copied"",
  ""lan.check"": ""Check"",
  ""lan.invite_valid"": ""valid for {0} min"",
  ""lan.invite_ready"": ""Invite code ready - copy the whole string and send it over"",
  ""lan.invite_copied"": ""Invite code copied"",
  ""lan.invite_need_endpoint"": ""Read your local port first"",
  ""lan.invite_stale"": ""World address changed - regenerate the code"",
  ""lan.invite_from"": ""Invite from {0} - check {1}"",
  ""lan.err_expired"": ""Code expired (10 min) - ask for a new one"",
  ""lan.err_broken"": ""Code looks truncated or edited - ask for a fresh copy"",
  ""lan.err_version"": ""Code from an incompatible version - update both launchers"",
  ""lan.err_endpoint"": ""Bad address - expected host:port (e.g. 192.168.1.5:52931)"",
  ""lan.err_endpoint_fmt"": ""Bad address: {0}"",
  ""lan.err_generic"": ""Unrecognized code - check that you copied all of it"",
  ""lan.detect_scanning"": ""No port in the log - listening for LAN broadcast for 3s..."",
  ""lan.detect_fail"": ""No port found. Open the world to LAN in-game first."",
  ""lan.detect_ok"": ""Local world address"",
  ""lan.copy_none"": ""Nothing to share yet"",
  ""lan.copy_done"": ""Invite text copied"",
  ""lan.copy_fail"": ""Copy failed - select the text and copy manually"",
  ""lan.join_need_addr"": ""Enter a world address or paste an invite code"",
  ""lan.joining"": ""Joining"",
  ""seasonal.hub_loading"": ""Festival config is not ready yet, please refresh later"",
  ""seasonal.hub_empty"": ""No festival content to show right now"",
  ""seasonal.hub_summary"": ""{0} events · {1} servers · {2} picks"",
  ""seasonal.current"": ""Current festival"",
  ""seasonal.none"": ""No festival period is active"",
  ""seasonal.events_title"": ""Limited-time Events"",
  ""seasonal.servers_title"": ""Festival Servers"",
  ""seasonal.picks_title"": ""Festival Picks"",
  ""seasonal.events_empty"": ""No limited-time events right now"",
  ""seasonal.servers_empty"": ""No seasonal servers yet"",
  ""seasonal.picks_empty"": ""No seasonal picks yet"",
  ""seasonal.open_link"": ""Open"",
  ""seasonal.copy_address"": ""Copy Address"",
  ""seasonal.copied"": ""Server address copied"",
  ""seasonal.copy_failed"": ""Copy failed"",
  ""seasonal.announcement.title"": ""Festival Notice"",
  ""seasonal.kind.mod"": ""Mod"",
  ""seasonal.kind.modpack"": ""Modpack"",
  ""seasonal.kind.resourcepack"": ""Resource Pack"",
  ""seasonal.kind.shader"": ""Shader"",
  ""seasonal.kind.version"": ""Version"",
  ""seasonal.kind.other"": ""Other"",
  ""seasonal.effects_hint"": ""Festival effects and content are driven by remote config; you can turn them off in Settings → Appearance."",
  ""settings.seasonal_effects"": ""Seasonal effects"",
  ""settings.java_path_hint"": ""This is only a preferred Java; the launcher still avoids Java versions unsupported by each Minecraft version automatically, so no manual switching is needed."",
  ""settings.use_littleskin"": ""Use LittleSkin"",
  ""settings.use_littleskin_tip"": ""Fill in the LittleSkin external login server address automatically"",
  ""settings.authlib_url_ph"": ""Server address, e.g. https://auth.example.com"",
  ""settings.authlib_email_ph"": ""Account email on that server""
}";
                    private static string BuiltInZhTW() => @"{
  ""app.title"": ""燧石啟動器"",
  ""app.launcher"": ""燧石啟動器"",
  ""tab.launch"": ""啟動遊戲"",
  ""tab.install"": ""安裝版本"",
  ""tab.download"": ""下載中心"",
  ""tab.settings"": ""設置"",
  ""tab.crash"": ""崩潰分析"",
  ""tab.mods"": ""Mod 管理"",
  ""tab.accounts"": ""賬號管理"",
  ""tab.skin"": ""皮膚預覽"",
  ""btn.launch"": ""啟動遊戲"",
  ""btn.install"": ""安裝"",
  ""btn.cancel"": ""取消"",
  ""btn.save"": ""保存"",
  ""btn.refresh"": ""刷新"",
  ""btn.search"": ""搜索"",
  ""btn.download"": ""下載"",
  ""btn.login"": ""登錄"",
  ""btn.logout"": ""登出"",
  ""btn.add_account"": ""添加賬號"",
  ""btn.delete"": ""刪除"",
  ""btn.check_updates"": ""檢查更新"",
  ""btn.check_deps"": ""依賴檢查"",
  ""lbl.version"": ""版本"",
  ""lbl.memory"": ""內存"",
  ""lbl.username"": ""用戶名"",
  ""lbl.java_path"": ""Java 路徑"",
  ""lbl.game_dir"": ""遊戲目錄"",
  ""lbl.install_type"": ""安裝類型"",
  ""lbl.vanilla"": ""原版"",
  ""lbl.fabric"": ""Fabric"",
  ""lbl.forge"": ""Forge"",
  ""lbl.modpack"": ""整合包"",
  ""lbl.modrinth_pack"": ""Modrinth 整合包"",
  ""lbl.account_type"": ""賬號類型"",
  ""lbl.offline"": ""離線"",
  ""lbl.microsoft"": ""微軟"",
  ""lbl.authlib"": ""Authlib-Injector"",
  ""lbl.theme"": ""主題"",
  ""lbl.language"": ""語言"",
  ""lbl.light"": ""亮色"",
  ""lbl.dark"": ""暗色"",
  ""lbl.chinese"": ""簡體中文"",
  ""lbl.english"": ""English"",
  ""lbl.no_mods"": ""未找到已安裝的 Mod"",
  ""lbl.no_deps_issues"": ""未檢測到依賴問題"",
  ""lbl.deps_ok"": ""所有依賴已滿足"",
  ""lbl.missing_deps"": ""缺失依賴"",
  ""lbl.conflict_deps"": ""衝突 Mod"",
  ""lbl.required"": ""必需"",
  ""lbl.optional"": ""可選"",
  ""lbl.search_mods"": ""搜索 Mod、光影、材質包..."",
  ""lbl.crash_analysis"": ""崩潰分析"",
  ""lbl.no_crash"": ""未檢測到崩潰報告"",
  ""msg.installing"": ""正在安裝 {0}..."",
  ""msg.install_done"": ""{0} 安裝完成"",
  ""msg.install_failed"": ""{0} 安裝失敗"",
  ""msg.downloading"": ""正在下載 ({0}/{1})..."",
  ""msg.launching"": ""正在啟動 {0}..."",
  ""msg.crashed"": ""遊戲崩潰：{0}"",
  ""msg.normal_exit"": ""遊戲正常退出"",
  ""msg.dep_missing"": ""缺少依賴：{0} ({1})"",
  ""msg.dep_conflict"": ""衝突：{0}（已安裝 {1}，衝突範圍 {2}）"",
  ""msg.skin_fetch_failed"": ""獲取皮膚失敗"",
  ""msg.ms_login_hint"": ""請在瀏覽器中打開 {0} 並輸入代碼 {1}"",
  ""msg.authlib_login_failed"": ""Authlib-Injector 登錄失敗"",
  ""crash.policy"": ""崩潰自動修復"",
  ""crash.policy.always"": ""始終開啟"",
  ""crash.policy.ask"": ""每次詢問"",
  ""crash.policy.never"": ""始終拒絕"",
  ""crash.repairable"": ""檢測到可自動修復的問題"",
  ""crash.not_repairable"": ""無法自動修復（需手動處理）"",
  ""crash.btn_repair"": ""嘗試自動修復"",
  ""crash.repairing"": ""正在嘗試自動修復…"",
  ""crash.repaired_success"": ""已修復併成功啟動遊戲！"",
  ""crash.repaired_recrash"": ""已嘗試修復，但遊戲再次崩潰，可繼續嘗試。"",
  ""crash.repair_unrepairable"": ""已嘗試修復但仍崩潰，且無法繼續自動修復。"",
  ""crash.repair_failed"": ""自動修復失敗：{0}"",
  ""crash.non_destructive"": ""所有修復操作均不會刪除或修改遊戲原文件。"",
  ""crash.analyzing"": ""正在分析崩潰報告…"",
  ""crash.open_report"": ""打開崩潰分析報告"",
  ""tab.game"": ""遊戲"",
  ""tab.toolbox"": ""工具箱"",
  ""tab.minecraft"": ""Minecraft"",
  ""tab.shader"": ""光影"",
  ""tab.resourcepack"": ""材質包"",
  ""tab.map"": ""地圖"",
  ""tab.install"": ""安裝"",
  ""status.java"": ""Java:"",
  ""status.installed"": ""已安裝 {0} 個版本"",
  ""status.running"": ""運行 {0} 個實例"",
  ""status.no_java"": ""未檢測到 Java"",
  ""status.java_low"": ""Java {0}（低於所需 {1}）"",
  ""status.network_ok"": ""網絡正常"",
  ""status.network_slow"": ""網絡延遲"",
  ""status.network_offline"": ""離線"",
  ""game.quick_launch"": ""快速啟動"",
  ""game.lan"": ""局域網遊戲"",
  ""game.servers"": ""服務器列表"",
  ""game.recommend"": ""智能推薦"",
  ""game.stats"": ""統計"",
  ""game.no_lan"": ""未發現局域網遊戲，點擊刷新掃描"",
  ""game.no_servers"": ""暫無服務器，點擊添加"",
  ""game.add_server"": ""添加服務器"",
  ""game.join"": ""加入"",
  ""game.edit"": ""編輯"",
  ""game.password_protected"": ""需要密碼"",
  ""game.players"": ""{0} 人"",
  ""game.latency"": ""延遲"",
  ""game.ping_good"": ""良好"",
  ""game.ping_ok"": ""一般"",
  ""game.ping_bad"": ""較差"",
  ""game.install_mod"": ""一鍵安裝"",
  ""game.not_interested"": ""不感興趣"",
  ""game.dep_missing_red"": ""缺失依賴（紅色標記）"",
  ""game.recent_version"": ""最近版本"",
  ""game.weekly_time"": ""本週時長"",
  ""game.crash_count"": ""崩潰次數"",
  ""game.annual_report"": ""年度報告"",
  ""game.mode_survival"": ""生存"",
  ""game.mode_creative"": ""創造"",
  ""game.mode_adventure"": ""冒險"",
  ""game.mode_spectator"": ""旁觀"",
  ""game.btn_start"": ""啟動"",
  ""game.account"": ""賬號"",
  ""game.no_account"": ""（無賬號，使用離線暱稱）"",
  ""game.username"": ""用戶名"",
  ""game.memory"": ""內存 MB"",
  ""dl.search_hint"": ""搜索 Mod、光影、材質包..."",
  ""dl.filter_version"": ""版本過濾"",
  ""dl.filter_loader"": ""加載器過濾"",
  ""dl.queue"": ""下載隊列"",
  ""dl.pause"": ""暫停"",
  ""dl.retry"": ""重試"",
  ""dl.pause_all"": ""全部暫停"",
  ""dl.resume_all"": ""繼續佇列"",
  ""dl.speed_limit"": ""限速"",
  ""dl.speed_limit_tip"": ""0 表示不限速；限速對全部下載生效"",
  ""dl.auto_retry"": ""失敗自動重試"",
  ""dl.resume"": ""繼續"",
  ""dl.clear_queue"": ""清空隊列"",
  ""dl.no_results"": ""未找到結果"",
  ""dl.install_modpack"": ""安裝整合包"",
  ""dl.browse_modrinth"": ""瀏覽 Modrinth"",
  ""dl.category"": ""分類"",
  ""dl.sort"": ""排序"",
  ""dl.sort_hot"": ""熱門"",
  ""dl.sort_new"": ""最新"",
  ""dl.sort_downloads"": ""下載量"",
  ""dl.map_views"": ""{0} 次瀏覽"",
  ""dl.map_author"": ""作者: {0}"",
  ""dl.detail"": ""詳情"",
  ""dl.extra_resources"": ""附加資源"",
  ""dl.downloading"": ""下載中"",
  ""dl.completed"": ""已完成"",
  ""tool.log"": ""日誌管理器"",
  ""tool.crash"": ""崩潰分析"",
  ""tool.perf"": ""性能監控"",
  ""tool.network"": ""網絡診斷"",
  ""tool.filewatch"": ""文件變更檢測"",
  ""tool.datapack"": ""數據包衝突檢測"",
  ""tool.saves"": ""存檔管理器"",
  ""tool.backup"": ""備份管理器"",
  ""tool.screenshot"": ""截圖管理器"",
  ""tool.clean"": ""清理冗餘文件"",
  ""tool.modpackio"": ""整合包導入導出"",
  ""tool.music"": ""音樂播放器"",
  ""tool.moddev"": ""Mod 開發環境"",
  ""tool.packmaker"": ""資源包創建器"",
  ""tool.nbt"": ""NBT 編輯器"",
  ""tool.command"": ""命令語法表"",
  ""tool.map"": ""地圖安裝"",
  ""tool.skin"": ""皮膚編輯器"",
  ""tool.seasonal"": ""節日中心"",
  ""tool.shortcut"": ""快捷方式生成器"",
  ""tool.afk"": ""掛機工作流"",
  ""tool.aichat"": ""AI 助手"",
  ""tool.group.diag"": ""診斷與排障"",
  ""tool.group.resource"": ""資源與內容"",
  ""tool.group.dev"": ""開發工具"",
  ""tool.group.other"": ""其他"",
  ""settings.general"": ""通用"",
  ""settings.launch"": ""啟動"",
  ""settings.recommend"": ""推薦"",
  ""settings.account"": ""賬號"",
  ""settings.music"": ""音樂"",
  ""music.set_auto_duck"": ""遊戲啟動時自動降低音樂音量"",
  ""music.set_resume"": ""啟動器啟動時自動續播上次音樂"",
  ""music.set_client_section"": ""本機用戶端模式"",
  ""music.set_client_section_tip"": ""由外部音樂用戶端播放，啟動器只負責在切走音源且用戶端持續暫停後回收它。關閉總開關後完全不涉及外部用戶端進程。"",
  ""music.set_client_enabled"": ""啟用本機用戶端模式"",
  ""music.set_client_grace"": ""用戶端寬限期"",
  ""music.set_client_grace_tip"": ""播放器切到本機文件夾或 API 模式，且所有由啟動器拉起的用戶端都處於暫停狀態時，持續此時間後直接結束這些用戶端進程。設為 0 表示不自動關閉。"",
  ""music.set_lyric_pin"": ""歌詞固定方式"",
  ""music.set_lyric_pin_tip"": ""只影響歌詞疊加層的渲染行數。"",
  ""music.set_lyric_pin_current"": ""僅當前行"",
  ""music.set_lyric_pin_next"": ""當前行 + 下一行"",
  ""music.set_lyric_pin_prev"": ""上一行 + 當前行 + 下一行"",
  ""music.set_client_lyric"": ""用戶端模式下仍用 API 取得歌詞"",
  ""music.set_client_lyric_tip"": ""開啟後，本機用戶端模式下疊加層仍顯示歌詞（依歌名從線上歌詞庫比對）；關閉則隱藏。"",
  ""music.set_client_app"": ""用戶端程式"",
  ""music.set_client_tip"": ""提示：本機用戶端模式下啟動器不輸出音訊，切回本機文件夾或 API 模式後，寬限期到期會自動結束由啟動器拉起的用戶端。"",
  ""music.source_api"": ""線上"",
  ""music.api_search"": ""搜尋"",
  ""music.api_keyword_tip"": ""輸入歌曲名或「歌名 歌手」"",
  ""music.api_playlist_id"": ""歌單 ID"",
  ""music.api_playlist_id_tip"": ""在音樂客戶端裡分享歌單，連結末尾的數字就是 ID"",
  ""music.api_import"": ""匯入"",
  ""music.api_my_playlists"": ""我的歌單"",
  ""music.api_refresh"": ""重新整理"",
  ""music.api_login"": ""掃碼登入"",
  ""music.api_logout"": ""登出"",
  ""music.api_cancel"": ""取消"",
  ""music.set_api_kind_vendor"": ""廠家自建 API（掃碼登入）"",
  ""music.api_account"": ""帳號"",
  ""music.api_scan_tip"": ""請用手機 App 掃描 QR Code 並確認登入"",
  ""music.api_results"": ""搜尋結果"",
  ""music.api_playlists_section"": ""歌單"",
  ""music.set_api_section"": ""線上音源"",
  ""music.set_api_section_tip"": ""由啟動器自行透過網路介面取流播放。預設指向 Meting 公共實例；若要使用「我的歌單」，請改用網易雲 API 並填寫自建服務位址。"",
  ""music.set_api_enabled"": ""啟用線上音源"",
  ""music.set_api_platform_kuwo"": ""酷我音樂"",
  ""music.set_api_platform_baidu"": ""百度音樂"",
  ""music.set_api_platform_xiami"": ""蝦米音樂"",
  ""music.set_api_platform_all"": ""全部平台（聚合搜尋）"",
  ""music.set_api_kind"": ""資料來源"",
  ""music.set_api_kind_meting"": ""Meting 聚合（免登入）"",
  ""music.set_api_kind_netease"": ""網易雲 API（支援掃碼登入）"",
  ""music.set_api_baseurl"": ""服務位址"",
  ""music.set_api_baseurl_tip"": ""不含結尾斜線。切換資料來源時會重設為對應的預設值。"",
  ""music.set_api_default"": ""恢復預設"",
  ""music.set_api_platform"": ""平台"",
  ""music.set_api_platform_netease"": ""網易雲音樂"",
  ""music.set_api_platform_tencent"": ""QQ 音樂"",
  ""music.set_api_platform_kugou"": ""酷狗音樂"",
  ""music.set_api_quality"": ""音質"",
  ""music.set_api_quality_standard"": ""標準 128k"",
  ""music.set_api_quality_higher"": ""較高 192k"",
  ""music.set_api_quality_exquisite"": ""極高 320k"",
  ""music.set_api_quality_lossless"": ""無損"",
  ""music.set_api_account"": ""登入狀態"",
  ""music.set_api_login_unsupported"": ""目前的資料來源不支援掃碼登入"",
  ""settings.ai"": ""AI"",
  ""settings.appearance"": ""外觀"",
  ""settings.about"": ""關於"",
  ""settings.lang"": ""語言"",
  ""settings.download"": ""下載"",
  ""settings.autostart"": ""開機自啟"",
  ""settings.minimize_tray"": ""最小化到托盤"",
  ""settings.animations"": ""動畫效果"",
  ""settings.toast_duration"": ""通知停留時長（秒）"",
  ""settings.filewatch"": ""文件變更檢測"",
  ""settings.default_memory"": ""默認內存 (MB)"",
  ""settings.default_username"": ""默認用戶名"",
  ""settings.java_path"": ""Java 路徑"",
  ""settings.extra_jvm"": ""額外 JVM 參數"",
  ""settings.repair_policy"": ""修復策略"",
  ""settings.repair_always"": ""始終自動修復"",
  ""settings.repair_ask"": ""每次詢問"",
  ""settings.repair_never"": ""拒絕修復"",
  ""settings.java_vendor"": ""Java 廠商"",
  ""settings.java_auto"": ""自動選擇"",
  ""settings.prewarm"": ""啟動預熱"",
  ""settings.hud"": ""HUD 疊加"",
  ""settings.launch_compat"": ""啟動前兼容性檢測"",
  ""settings.dl_mirror"": ""鏡像策略"",
  ""settings.dl_mirror_auto"": ""自動"",
  ""settings.dl_mirror_bmcl"": ""BMCLAPI 優先"",
  ""settings.dl_mirror_official"": ""僅官方"",
  ""settings.dl_concurrent"": ""最大併發下載數"",
  ""settings.auto_deps"": ""自動安裝缺失前置"",
  ""settings.auto_deps_always"": ""始終"",
  ""settings.auto_deps_ask"": ""詢問"",
  ""settings.auto_deps_off"": ""關閉"",
  ""settings.repair_rp"": ""資源包自動修復"",
  ""settings.server_pack_cache"": ""服務器資源包緩存"",
  ""settings.recommend_mode"": ""推薦模式"",
  ""settings.recommend_on"": ""啟用（本地+在線）"",
  ""settings.recommend_local"": ""僅本地規則"",
  ""settings.recommend_off"": ""禁用"",
  ""settings.gameplay_prefs"": ""玩法偏好"",
  ""settings.add_offline"": ""添加離線賬號"",
  ""settings.offline_name"": ""玩家名"",
  ""settings.login_ms"": ""Microsoft 登錄"",
  ""settings.add_authlib"": ""添加 Authlib 賬號"",
  ""settings.authlib_url"": ""服務器地址"",
  ""settings.authlib_email"": ""郵箱"",
  ""settings.authlib_password"": ""密碼"",
  ""settings.ai_enable"": ""啟用 AI 助手"",
  ""settings.ai_mode"": ""部署方式"",
  ""settings.ai_external"": ""外部 API"",
  ""settings.ai_local"": ""本地 Ollama"",
  ""settings.ai_endpoint"": ""API 地址"",
  ""settings.ai_key"": ""API Key"",
  ""settings.ai_model"": ""模型"",
  ""settings.ai_test"": ""測試連接"",
  ""settings.ai_status_ok"": ""服務正常"",
  ""settings.ai_status_warn"": ""響應緩慢"",
  ""settings.ai_status_err"": ""無法連接"",
  ""settings.ai_crash"": ""崩潰解讀"",
  ""settings.ai_recommend"": ""推薦理由"",
  ""settings.ai_translate"": ""Mod 翻譯"",
  ""settings.theme_color"": ""主題色"",
  ""settings.pick_color"": ""取色…"",
  ""settings.background"": ""背景圖"",
  ""settings.font_scale"": ""字體大小"",
  ""settings.tab_colors"": ""索引貼顏色"",
  ""settings.tab_game"": ""遊戲標籤"",
  ""settings.tab_download"": ""下載標籤"",
  ""settings.tab_toolbox"": ""工具箱標籤"",
  ""settings.tab_settings"": ""設置標籤"",
  ""settings.high_dpi"": ""適配高分辨率屏幕（啟用 2x 圖標）"",
  ""about.version"": ""版本"",
  ""about.check_update"": ""檢查更新"",
  ""about.license"": ""開源許可"",
  ""about.github"": ""GitHub 倉庫"",
  ""btn.ok"": ""確定"",
  ""btn.yes"": ""是"",
  ""btn.no"": ""否"",
  ""btn.close"": ""關閉"",
  ""btn.add"": ""添加"",
  ""btn.edit"": ""編輯"",
  ""btn.remove"": ""移除"",
  ""btn.back"": ""返回"",
  ""btn.next"": ""下一步"",
  ""btn.finish"": ""完成"",
  ""btn.export"": ""導出"",
  ""btn.import"": ""導入"",
  ""btn.apply"": ""應用"",
  ""btn.reset"": ""重置"",
  ""btn.copy"": ""複製"",
  ""btn.paste"": ""粘貼"",
  ""btn.browse"": ""瀏覽..."",
  ""btn.clear"": ""清除"",
  ""color.lightness"": ""亮度"",
  ""color.palette"": ""完整色板"",
  ""color.plane"": ""色域（橫：色相 · 縱：飽和度）"",
  ""msg.loading"": ""加載中..."",
  ""msg.no_data"": ""暫無數據"",
  ""msg.success"": ""操作成功"",
  ""msg.failed"": ""操作失敗"",
  ""msg.confirm_delete"": ""確認刪除？此操作不可撤銷。"",
  ""msg.saving"": ""保存中..."",
  ""msg.saved"": ""已保存"",
  ""msg.connected"": ""已連接"",
  ""msg.disconnected"": ""未連接"",
  ""msg.connecting"": ""連接中..."",
  ""status.ready"": ""就緒"",
  ""home.game.desc"": ""啟動器主頁（版本選擇 / 啟動遊戲 / 實例管理）。"",
  ""theme.editor.title"": ""四色主題自定義（Core.TabThemeConfig）"",
  ""theme.editor.hint"": ""修改後立即反映到標題欄與側欄選中色；非法值（非 #RRGGBB）會被 Core 自動回退默認色。"",
  ""java.title"": ""Java 環境檢測"",
  ""java.detect"": ""檢測本機 Java"",
  ""java.scanning"": ""正在掃描 Java（JAVA_HOME / /usr/lib/jvm / /opt/java / PATH）..."",
  ""java.detected"": ""檢測到 {0} 個 Java 安裝"",
  ""tool.log.desc"": ""查看 / 搜索 / 過濾 / 導出遊戲日誌與崩潰報告。"",
  ""tool.clean.desc"": ""掃描並清理遊戲目錄中的冗餘與殘留文件。"",
  ""tool.backup.desc"": ""管理存檔與配置的手動 / 自動備份。"",
  ""tool.screenshot.desc"": ""瀏覽、刪除與打包遊戲截圖。"",
  ""tool.crash.desc"": ""分析崩潰報告，定位異常類型與修復建議。"",
  ""tool.datapack.desc"": ""掃描數據包衝突與格式問題，給出處理建議。"",
  ""tool.saves.desc"": ""管理存檔：兼容性與損壞檢測、降級與備份。"",
  ""tool.skin.desc"": ""預覽與編輯玩家皮膚，校驗尺寸與模型。"",
  ""tab.minecraft.desc"": ""下載與安裝 Minecraft 遊戲版本。"",
  ""tab.mods.desc"": ""瀏覽、安裝與管理 Mod，處理依賴與衝突。"",
  ""tab.shader.desc"": ""光影包的安裝、預覽與配置。"",
  ""tab.resourcepack.desc"": ""材質包的安裝、預覽與管理。"",
  ""tab.modpack.desc"": ""整合包的導入、安裝與升級。"",
  ""tab.map.desc"": ""地圖存檔的下載與導入。"",
  ""tool.perf.desc"": ""實時性能監控與 FPS / TPS 分析。"",
  ""tool.network.desc"": ""網絡連接與登錄服務器診斷。"",
  ""tool.filewatch.desc"": ""監控遊戲目錄文件變更。"",
  ""tool.modpackio.desc"": ""整合包的導入與導出。"",
  ""tool.music.desc"": ""背景音樂與音軌管理。"",
  ""tool.moddev.desc"": ""模組開發輔助（模板 / 調試）。"",
  ""tool.packmaker.desc"": ""整合包製作與打包。"",
  ""tool.nbt.desc"": ""NBT 數據結構編輯器。"",
  ""tool.command.desc"": ""生成複雜命令與函數。"",
  ""tool.shortcut.desc"": ""生成桌面 / 開始菜單快捷方式。"",
  ""tool.afk.desc"": ""掛機與輕量自動化。"",
  ""tool.aichat.desc"": ""對接 Core.Ai 的聊天助手。"",
  ""settings.general.desc"": ""啟動器的常規行為與外觀設置。"",
  ""settings.launch.desc"": ""遊戲啟動參數、Java 與內存設置。"",
  ""settings.download.desc"": ""下載源、併發與緩存設置。"",
  ""settings.recommend.desc"": ""推薦內容與個性化設置。"",
  ""settings.account.desc"": ""賬號登錄與多賬號管理。"",
  ""settings.ai.desc"": ""AI 助手相關配置。"",
  ""settings.appearance.desc"": ""主題與四色配色自定義。"",
  ""settings.about.desc"": ""關於燧石啟動器與版本信息。"",
  ""tool.versionlist"": ""版本列表"",
  ""tool.versionlist.desc"": ""管理已安裝的遊戲版本，選擇並一鍵啟動。"",
  ""tool.devtools"": ""開發工具"",
  ""tool.devtools.desc"": ""Mod 骨架生成、資源包創建器與命令速查表。"",
  ""version.settings.title"": ""版本設置"",
  ""version.display_name"": ""顯示名"",
  ""version.isolation"": ""隔離模式"",
  ""isolation.shared"": ""共享"",
  ""isolation.auto"": ""自動隔離"",
  ""isolation.custom"": ""自定義目錄"",
  ""version.effective_dir"": ""有效工作目錄"",
  ""version.mod_loader"": ""模組加載器"",
  ""version.install_loader"": ""安裝加載器"",
  ""version.lock"": ""版本鎖定"",
  ""version.lock.desc"": ""鎖定後阻止自動更新覆蓋該版本"",
  ""version.resolution"": ""分辨率與窗口"",
  ""version.fullscreen"": ""全屏啟動"",
  ""version.java"": ""Java 與性能"",
  ""version.max_memory"": ""最大內存"",
  ""version.min_memory"": ""最小內存"",
  ""version.extra_jvm"": ""額外 JVM 參數"",
  ""mods.manage"": ""模組與資源包管理"",
  ""mods.installed"": ""已安裝"",
  ""mods.add"": ""添加"",
  ""mods.remove"": ""移除"",
  ""mods.refresh"": ""刷新"",
  ""mods.check_update"": ""檢查更新"",
  ""mods.search"": ""搜索"",
  ""mods.open_folder"": ""打開文件夾"",
  ""resourcepack"": ""資源包"",
  ""shader"": ""光影"",
  ""install.fabric"": ""安裝 Fabric"",
  ""install.forge"": ""安裝 Forge"",
  ""install.neoforge"": ""安裝 NeoForge"",
  ""install.quilt"": ""安裝 Quilt"",
  ""crash.view.title"": ""崩潰分析報告"",
  ""crash.keep_mod_hint"": ""請選擇要保留的 Mod（其餘將重命名為 .disabled）："",
  ""crash.missing_dep_hint"": ""缺失的前置依賴（將自動下載安裝）："",
  ""crash.downgrade_cause"": ""崩潰疑似由存檔降級引起"",
  ""crash.revert_backup"": ""回滾到備份"",
  ""crash.retry_other"": ""改用其他方式"",
  ""crash.install_original"": ""安裝存檔原版本"",
  ""crash.downgrade_note"": ""所有操作均不刪除原存檔：回滾會把當前檔另存，原檔始終保留在備份目錄。"",
  ""crash.causes_label"": ""可能原因："",
  ""crash.suggestions_label"": ""修復建議："",
  ""crash.tab.causes"": ""原因與建議"",
  ""crash.tab.full"": ""完整崩潰報告"",
  ""savecompat.title"": ""存檔版本不兼容"",
  ""savecompat.detected"": ""檢測到以下存檔的版本高於當前要啟動的遊戲版本："",
  ""savecompat.downgrade"": ""降級這些存檔"",
  ""savecompat.install_version"": ""安裝對應版本"",
  ""savecompat.ignore"": ""仍要啟動（忽略）"",
  ""update.found"": ""發現新版本"",
  ""update.emergency"": ""緊急更新：此版本含重要修復或安全更新，建議立即安裝"",
  ""update.later"": ""稍後"",
  ""update.download"": ""下載更新"",
  ""update.download_all"": ""全部更新"",
  ""update.gui_only"": ""只更新介面"",
  ""update.gui_only_tip"": ""只更新啟動器介面，不下載命令列工具（體積更小）"",
  ""update.fetching_cli"": ""正在下載命令列工具…"",
  ""update.downloading_cli_pct"": ""命令列工具下載中 {0}%…"",
  ""update.preparing"": ""準備下載…"",
  ""lbl.traditional"": ""繁體中文"",
  ""update.version_line"": ""當前 {0} → 最新 {1}"",
  ""update.emgent_note"": ""（緊急更新，請儘快安裝）"",
  ""update.recommend_note"": ""（建議立即更新）"",
  ""update.fetching"": ""正在通過內置下載器獲取更新包…"",
  ""update.applying"": ""下載完成，正在應用更新…"",
  ""update.cancelled"": ""已取消更新"",
  ""update.failed"": ""更新失敗：{0}"",
  ""update.downloading_pct"": ""下載中… {0}%"",
  ""update.no_changelog"": ""（無法獲取更新日誌，請點擊下方「下載更新」在發佈頁查看詳情）"",
  ""settings.music_autoduck"": ""遊戲啟動時音樂自動降音量"",
  ""settings.music_resume"": ""啟動器啟動時自動續播上次音樂"",
  ""settings.default_isolation"": ""新建版本默認隔離："",
  ""settings.isolation_shared_label"": ""共享（同一 .minecraft）"",
  ""settings.isolation_auto_label"": ""隔離（各自 versions/<id>）"",
  ""settings.game_dir_label"": ""遊戲目錄："",
  ""settings.game_dir_tip"": ""Minecraft 遊戲目錄（.minecraft）。留空表示使用系統默認目錄。"",
  ""settings.memory_mb"": ""內存(MB)："",
  ""settings.game_volume"": ""遊戲降音量"",
  ""settings.game_volume_tip"": ""開啟後每次啟動遊戲都會在原有音量基礎上降低右側百分比（不改動 options.txt）"",
  ""settings.offline_username"": ""離線用戶名："",
  ""settings.jvm_args"": ""JVM 參數："",
  ""settings.crash_autorepair_label"": ""崩潰自動修復："",
  ""settings.java_source"": ""Java 源："",
  ""settings.java_temurin"": ""Temurin"",
  ""settings.java_oracle"": ""Oracle"",
  ""settings.missing_dep_label"": ""缺失前置："",
  ""settings.on_label"": ""開啟"",
  ""settings.hud_label"": ""HUD 疊加顯示"",
  ""settings.global_hotkeys"": ""全域快捷鍵"",
  ""settings.global_hotkeys_tip"": ""Ctrl+Alt+Enter 快速啟動上次遊玩的版本；Ctrl+Alt+M 呼出 / 收起啟動器視窗（系統級，啟動器不在前景也生效）"",
  ""settings.hud_config"": ""HUD 設定"",
  ""settings.hud_fields"": ""顯示內容"",
  ""settings.hud_anchor"": ""停靠位置"",
  ""settings.hud_anchor_tl"": ""左上"",
  ""settings.hud_anchor_tr"": ""右上"",
  ""settings.hud_anchor_bl"": ""左下"",
  ""settings.hud_anchor_br"": ""右下"",
  ""settings.hud_font_size"": ""字型大小"",
  ""settings.hud_opacity"": ""不透明度"",
  ""settings.hud_refresh"": ""更新間隔"",
  ""settings.hud_margin"": ""螢幕邊距"",
  ""settings.hud_click_through"": ""滑鼠穿透"",
  ""settings.hud_foreground_only"": ""僅遊戲前景顯示"",
  ""settings.hud_background"": ""半透明背板"",
  ""settings.hud_reset"": ""恢復預設"",
  ""settings.hud_tip"": ""提示：直接拖動遊戲內的 HUD 即可自訂位置，拖動後自動記為「自訂位置」。勾選「滑鼠穿透」後 HUD 不會攔截滑鼠操作。"",
  ""settings.hud_field_fps"": ""幀率"",
  ""settings.hud_field_memory"": ""記憶體"",
  ""settings.hud_field_cpu"": ""CPU"",
  ""settings.hud_field_ping"": ""延遲"",
  ""settings.hud_field_coords"": ""座標"",
  ""settings.ms_login_title"": ""微軟登入"",
  ""settings.ms_code_copied"": ""裝置代碼 {0} 已複製到剪貼簿，直接貼到瀏覽器即可"",
  ""settings.hud_field_biome"": ""生態域"",
  ""settings.hud_field_gametime"": ""遊戲內時間"",
  ""settings.hud_field_session"": ""本次時長"",
  ""settings.launch_compat_label"": ""啟動前存檔兼容性檢測"",
  ""settings.download_source"": ""下載源優先："",
  ""settings.cf_section"": ""CurseForge 接入"",
  ""settings.cf_key"": ""API Key"",
  ""settings.cf_key_hint"": ""留空則使用啟動器內建 Key（建置時注入）；也可用啟動參數 --curseforge-key 覆寫"",
  ""settings.cf_root"": ""API Root"",
  ""settings.cf_root_hint"": ""留空使用官方 https://api.curseforge.com，可填第三方鏡像位址"",
  ""settings.cf_test"": ""測試連線"",
  ""settings.cf_tip"": ""CurseForge 用於「下載 → 整合包」的線上來源與 Mod 指紋比對。作者關閉第三方分發的專案無法自動下載，啟動器會提示前往官網手動取得。"",
  ""settings.cf_status_disabled"": ""已關閉"",
  ""settings.cf_status_user"": ""正在使用自訂 Key"",
  ""settings.cf_status_builtin"": ""正在使用內建 Key"",
  ""settings.cf_status_none"": ""未設定 Key，CurseForge 來源不可用"",
  ""settings.cf_testing"": ""正在測試 CurseForge 連線…"",
  ""settings.cf_test_ok"": ""CurseForge 連線正常"",
  ""settings.cf_test_fail"": ""CurseForge 連線失敗：{0}"",
  ""settings.cf_test_unknown"": ""未知原因"",
  ""settings.mirror_first"": ""鏡像優先"",
  ""settings.official_first"": ""官方優先"",
  ""settings.max_concurrent"": ""最大併發下載："",
  ""settings.autorepair_rp_label"": ""進服時自動修復資源包問題"",
  ""settings.recommend_master"": ""推薦總開關："",
  ""settings.recommend_enable_label"": ""啟用推薦"",
  ""settings.account_mgmt"": ""賬號管理"",
  ""settings.ms_client_id"": ""Microsoft client_id："",
  ""settings.ms_client_id_tip"": ""填寫你的 Azure 應用 client_id（可選）；留空則使用內置默認 client_id。設備代碼流無需配置任何回跳地址。"",
  ""settings.ms_devicecode_desc"": ""微軟登錄採用設備代碼流（device code flow），無需配置任何回跳地址（redirect_uri），兼容性最好。可留空使用內置默認 client_id；如需使用自己的 Azure 應用，填入對應 client_id 即可。保存設置後點擊「登錄微軟」，按彈窗提示在瀏覽器輸入設備代碼完成登錄。"",
  ""settings.set_current"": ""設為當前"",
  ""settings.authlib_title"": ""Authlib-Injector 賬號"",
  ""settings.authlib_desc"": ""外置登錄：填入第三方 Authlib-Injector 服務器地址，使用該系統賬號（郵箱+密碼）登錄，可繞過微軟官方驗證。僅在你信任的私有/社區服務器下使用。微軟登錄失敗多為 Azure 應用 client_id 未配置或被拒，需檢查憑據。"",
  ""settings.authlib_hint1"": ""① 服務器地址：第三方 Authlib-Injector 的 URL，如 https://auth.example.com"",
  ""settings.authlib_hint2"": ""② 郵箱：該服務器的賬號郵箱"",
  ""settings.authlib_hint3"": ""③ 密碼：對應郵箱密碼"",
  ""settings.bg_image"": ""背景圖片："",
  ""settings.show_borders"": ""顯示控件邊框"",
  ""settings.follow_system_theme"": ""跟隨系統主題"",
  ""settings.follow_system_theme_tip"": ""開啟後啟動器亮 / 暗隨 Windows 系統主題自動切換"",
  ""settings.ui_style"": ""介面風格"",
  ""settings.ui_style_tip"": ""切換整體介面觀感；安卓式會把主導航移到底部"",
  ""settings.ui_style_standard"": ""標準（四色索引貼）"",
  ""settings.ui_style_android"": ""簡約安卓式"",
  ""settings.ui_style_glass"": ""簡約毛玻璃"",
  ""settings.ui_style_dynamic"": ""靈動"",
  ""settings.ui_style_switched"": ""已切換至"",
  ""settings.ui_style_pick"": ""選擇"",
  ""settings.ui_style_inuse"": ""使用中"",
  ""settings.window_bg_color"": ""視窗背景色"",
  ""settings.glass_blur"": ""顯示毛玻璃效果"",
  ""settings.glass_opacity"": ""毛玻璃不透明度"",
  ""settings.hide_sidebar_labels"": ""隱藏側邊欄圖示標籤"",
  ""settings.touch_mode"": ""觸控模式"",
  ""settings.touch_mode_tip"": ""開啟後隨遊戲行程顯示虛擬按鍵面板（預設含方向鍵與跳躍鍵）"",
  ""settings.touch_config"": ""虛擬按鍵佈局"",
  ""settings.touch_edit"": ""編輯佈局"",
  ""settings.touch_reset"": ""恢復預設"",
  ""settings.touch_tip"": ""在編輯器中拖動圓形按鍵調整位置，可增刪按鍵、修改對應鍵碼與大小；面板隨遊戲啟動顯示、遊戲結束自動關閉。"",
  ""settings.autoupdate_check"": ""啟動時自動檢查更新"",
  ""settings.license_github"": ""開源許可 · GitHub 倉庫"",
  ""settings.reset_default"": ""恢復默認"",
  ""settings.auto_detect"": ""自動檢測"",
  ""settings.open_label"": ""打開"",
  ""ai.m01"": ""燧石 AI 助手"",
  ""ai.m02"": ""我可以幫你分析崩潰日誌、推薦 Mod、翻譯描述、生成年度總結，也可以直接聊天。"",
  ""ai.m03"": ""自動分析最近一次崩潰日誌"",
  ""ai.m04"": ""把英文 Mod 描述譯為中文"",
  ""ai.m05"": ""配裝推薦"",
  ""ai.m06"": ""按你的玩法偏好推薦 Mod"",
  ""ai.m07"": ""年度總結"",
  ""ai.m08"": ""彙總你這一年的遊玩記錄"",
  ""ai.m09"": ""輸入你的問題…（可粘貼崩潰日誌）"",
  ""ai.m10"": ""發送"",
  ""ai.m11"": ""開啟後可選本地部署或外部 API，用於崩潰分析、Mod 推薦翻譯等場景。"",
  ""ai.m12"": ""外部 API（填 Key 即用，零下載、零佔用）"",
  ""ai.m13"": ""本地部署（自動安裝 Ollama + 輕量模型）"",
  ""ai.m14"": ""本地部署（Ollama）"",
  ""ai.m15"": ""一鍵安裝 Ollama"",
  ""ai.m16"": ""服務狀態："",
  ""ai.m17"": ""外部 API（OpenAI 兼容）"",
  ""ai.m18"": ""API 端點"",
  ""ai.m19"": ""模型名"",
  ""ai.m20"": ""AI 功能"",
  ""ai.m21"": ""崩潰日誌智能解讀"",
  ""ai.m22"": ""生成推薦理由"",
  ""ai.m23"": ""Mod 描述翻譯"",
  ""ai.m24"": ""清空對話"",
  ""annual.m24"": ""年份"",
  ""annual.m25"": ""年度稱號："",
  ""annual.m26"": ""複製 Token"",
  ""annual.m27"": ""導出 Markdown"",
  ""annual.m28"": ""總時長（小時）"",
  ""annual.m29"": ""啟動次數"",
  ""annual.m30"": ""活躍天數"",
  ""annual.m31"": ""最長連續（天）"",
  ""annual.m32"": ""單次最久"",
  ""annual.m33"": ""月度分佈（小時）"",
  ""annual.m34"": ""版本排行（小時）"",
  ""annual.m35"": ""一句話解讀"",
  ""annual.m36"": ""分享 Token（完全離線，複製發給好友，對方粘貼即可看你的年度報告）"",
  ""annual.m37"": ""把好友的分享 Token 粘貼到這裡"",
  ""annual.m38"": ""好友的"",
  ""annual.m39"": ""年度報告（稱號："",
  ""annual.m40"": ""總時長 "",
  ""annual.m41"": "" 小時 · "",
  ""annual.m42"": "" 次啟動 · 活躍 "",
  ""annual.m43"": "" 天 · 崩潰 "",
  ""annual.m44"": "" 次"",
  ""backup.m45"": ""備份策略"",
  ""backup.m46"": ""存儲路徑"",
  ""backup.m47"": ""相對路徑按遊戲目錄解析；也可填絕對路徑備份到移動硬盤"",
  ""backup.m48"": ""瀏覽…"",
  ""backup.m49"": ""打開目錄"",
  ""backup.m50"": ""定時備份"",
  ""backup.m51"": ""每來源保留"",
  ""backup.m52"": ""超出份數時自動刪除最舊的（0 = 不限）"",
  ""backup.m53"": ""份"",
  ""backup.m54"": ""自動備份保留"",
  ""backup.m55"": ""超過天數的自動備份會被清理（0 = 不按時間清理）；手動備份不受影響"",
  ""backup.m56"": ""天"",
  ""backup.m57"": ""恢復前自動備份當前狀態"",
  ""backup.m58"": ""啟動遊戲前自動備份存檔"",
  ""backup.m59"": ""保存策略"",
  ""backup.m60"": ""備份來源"",
  ""backup.m61"": ""備註"",
  ""backup.m62"": ""可選，方便日後辨認這份備份"",
  ""backup.m63"": ""立即備份"",
  ""backup.m64"": ""來源"",
  ""backup.m65"": ""類型"",
  ""backup.m66"": ""創建時間"",
  ""backup.m67"": ""大小"",
  ""backup.m68"": ""文件數"",
  ""backup.m69"": ""恢復選中"",
  ""backup.m70"": ""刪除選中"",
  ""backup.m71"": ""按策略清理"",
  ""cmd.m72"": ""常用語法表"",
  ""cmd.m73"": ""插入"",
  ""cmd.m74"": ""命令拼接 / 編輯"",
  ""crash.m75"": ""最近的崩潰報告"",
  ""crash.m76"": ""分析與建議"",
  ""crash.m77"": ""打開完整分析報告"",
  ""datapack.m78"": ""存檔"",
  ""datapack.m79"": ""目標版本"",
  ""datapack.m80"": ""填寫如 1.21，用於 pack_format 告警；留空則跳過該檢查"",
  ""datapack.m81"": ""掃描"",
  ""datapack.m82"": ""刷新存檔"",
  ""datapack.m83"": ""更新規則庫"",
  ""datapack.m84"": ""恢復內置"",
  ""datapack.m85"": ""生效："",
  ""datapack.m86"": ""　被覆蓋："",
  ""datapack.m87"": ""跳轉到數據包"",
  ""datapack.m88"": ""數據包（按加載順序）"",
  ""datapack.m89"": ""　缺 pack.mcmeta"",
  ""datapack.m90"": ""格式告警"",
  ""datapack.m91"": ""處理建議："",
  ""dev.m92"": ""命令速查"",
  ""dev.m93"": ""Mod 骨架"",
  ""dev.m94"": ""資源包創建"",
  ""dev.m95"": ""Mod 名稱"",
  ""dev.m96"": ""版本號"",
  ""dev.m97"": ""加載器"",
  ""dev.m98"": ""遊戲版本"",
  ""dev.m99"": ""包名（如 com.example.mymod）"",
  ""dev.m100"": ""生成項目骨架"",
  ""dev.m101"": ""新手教程"",
  ""dev.m102"": ""1. 輸入名稱和 pack_format（1.21=34, 1.20=15, 1.19=9）"",
  ""dev.m103"": ""2. 生成後在 assets/minecraft 下放入紋理/模型/聲音文件"",
  ""dev.m104"": ""3. 打包 zip 或直接放到 .minecraft/resourcepacks"",
  ""dev.m105"": ""資源包名稱"",
  ""dev.m106"": ""pack_format（MC版本對應的格式號）"",
  ""dev.m107"": ""生成資源包"",
  ""dl.m108"": ""下載中心 (Modrinth)"",
  ""dl.m109"": ""搜索關鍵詞"",
  ""dl.m110"": ""加入隊列"",
  ""dl.m111"": ""開始下載隊列"",
  ""dl.m112"": ""清空已完成"",
  ""dl.m113"": ""該地圖附帶資源包 / 光影，可在詳情窗一併安裝"",
  ""dl.m114"": ""含附加資源"",
  ""dl.m115"": ""整合包來源"",
  ""dl.m116"": ""版本類型"",
  ""dl.m117"": ""全部"",
  ""dl.m118"": ""正式版"",
  ""dl.m119"": ""快照"",
  ""dl.m120"": ""舊版"",
  ""dl.m121"": ""最新發布"",
  ""dl.m122"": ""最多瀏覽"",
  ""dl.m123"": ""上一頁"",
  ""dl.m124"": ""第 "",
  ""dl.m125"": "" 頁"",
  ""dl.m126"": ""下一頁"",
  ""dl.m127"": ""作者"",
  ""dl.m128"": ""瀏覽"",
  ""dl.m129"": ""點贊"",
  ""dl.m130"": ""評分"",
  ""dl.m131"": ""地圖站頁面"",
  ""dl.m132"": ""下載資源包 / 光影並自動分發到 resourcepacks、shaderpacks"",
  ""dl.m133"": ""下載到存檔"",
  ""dl.m134"": ""選擇版本"",
  ""dl.m135"": ""隔離安裝（獨立 versions/<包名> 目錄）"",
  ""dl.m136"": ""來源頁面"",
  ""dl.m137"": ""版本數"",
  ""dl.m138"": ""AI 翻譯"",
  ""dl.m139"": ""未啟用 AI 助手時不可用"",
  ""dl.m140"": ""項目頁面"",
  ""dl.m141"": ""安裝所選版本"",
  ""dl.m142"": ""安裝 Minecraft 版本"",
  ""dl.m143"": ""選擇加載器（可不選）"",
  ""dl.m144"": ""原版（不安裝加載器）"",
  ""dl.m145"": ""Fabric（自動配對最新 Fabric API）"",
  ""game.m146"": ""📦 版本庫"",
  ""game.m147"": ""打開版本列表大頁"",
  ""game.m148"": ""未發現局域網世界，請在遊戲內開啟「對局域網開放」後點擊刷新。"",
  ""game.m149"": ""服務器列表為空。"",
  ""game.m150"": ""刷新推薦"",
  ""game.m151"": ""依賴"",
  ""game.m152"": ""暫無推薦，可點擊「刷新推薦」。"",
  ""game.m153"": ""崩潰次數（年）"",
  ""game.m154"": ""查看"",
  ""game.m155"": ""今日可查看年度遊玩統計"",
  ""home.m156"": ""選擇版本："",
  ""home.m157"": ""遊玩統計"",
  ""home.m158"": ""累計時長"",
  ""home.m159"": ""為你推薦"",
  ""home.m160"": ""依賴補全"",
  ""install.m161"": ""安裝新版本"",
  ""install.m162"": ""類型："",
  ""install.m163"": ""版本："",
  ""log.m164"": ""僅錯誤"",
  ""map.m165"": ""安裝地圖"",
  ""moddev.m166"": ""顯示名稱"",
  ""moddev.m167"": ""目標 MC 版本"",
  ""moddev.m168"": ""目標目錄"",
  ""moddev.m169"": ""生成 Mod 骨架"",
  ""modpack.m170"": ""導出整合包"",
  ""modpack.m171"": ""顯示名稱（可選）"",
  ""modpack.m172"": ""包含 Mods"",
  ""modpack.m173"": ""包含 Config"",
  ""modpack.m174"": ""包含 資源包"",
  ""modpack.m175"": ""包含 光影包"",
  ""modpack.m176"": ""包含 存檔"",
  ""modpack.m177"": ""導出…"",
  ""modpack.m178"": ""導入整合包"",
  ""modpack.m179"": ""支持 Modrinth .mrpack 整合包。"",
  ""modpack.m180"": ""選擇文件並導入…"",
  ""music.m181"": ""音源："",
  ""music.m182"": ""本地文件夾"",
  ""music.m183"": ""在線流媒體"",
  ""music.m184"": ""MC 原聲"",
  ""music.client_mode"": ""本機用戶端"",
  ""music.client_title"": ""本機用戶端模式"",
  ""music.client_desc"": ""音樂由外部用戶端播放，啟動器不再輸出音訊。切換到其它音源後，若用戶端持續暫停超過設定時長會自動結束它。"",
  ""music.client_choose"": ""選擇程式"",
  ""music.client_launch"": ""啟動用戶端"",
  ""music.client_lyric_hint"": ""用戶端模式下歌詞依歌名從線上歌詞庫比對，若用戶端正在播放的歌曲不同則不會顯示。"",
  ""music.m185"": ""加載音樂文件夾"",
  ""music.m186"": ""在線流媒體地址（直接播放鏈接）"",
  ""music.m187"": ""預設："",
  ""music.m188"": ""掃描 MC 原聲"",
  ""music.m189"": ""刪除曲目"",
  ""music.col_name"": ""名稱"",
  ""music.col_title"": ""標題"",
  ""music.col_artist"": ""藝術家"",
  ""music.col_album"": ""專輯"",
  ""music.col_duration"": ""時長"",
  ""nbt.m190"": ""打開…"",
  ""nbt.m191"": ""另存為…"",
  ""nbt.m192"": ""導出文本"",
  ""nbt.m193"": ""保存時先把原文件複製一份 .bak"",
  ""nbt.m194"": ""保存前自動備份"",
  ""nbt.m195"": ""存檔的 level.dat 快捷入口"",
  ""nbt.m196"": ""全部展開"",
  ""nbt.m197"": ""全部摺疊"",
  ""nbt.m198"": ""選中節點"",
  ""nbt.m199"": ""路徑"",
  ""nbt.m200"": ""值（僅標量可編輯）"",
  ""nbt.m201"": ""應用修改"",
  ""nbt.m202"": ""新增 / 重命名"",
  ""nbt.m203"": ""名稱"",
  ""nbt.m204"": ""新增子標籤"",
  ""nbt.m205"": ""重命名"",
  ""nbt.m206"": ""刪除選中標籤"",
  ""net.m207"": ""重新檢測"",
  ""perf.m208"": ""運行實例與性能"",
  ""perf.m209"": ""系統 CPU 佔用"",
  ""perf.m210"": ""內存佔用"",
  ""perf.m211"": ""運行實例"",
  ""perf.m212"": ""CPU 邏輯核數"",
  ""perf.m213"": ""可用內存"",
  ""perf.m214"": ""累計遊玩(分鐘)"",
  ""perf.m215"": ""啟動時間"",
  ""clean.m216"": ""清理選中"",
  ""clean.m217"": ""直接刪除（不可還原）"",
  ""saves.m218"": ""存檔管理"",
  ""saves.m219"": ""目標遊戲版本："",
  ""saves.m220"": ""掃描兼容性"",
  ""saves.m221"": ""掃描損壞"",
  ""saves.m222"": ""(§二.4 檢測 / §三 降級與回滾 / 損壞檢測只檢測不修復)"",
  ""saves.m223"": ""當前版本："",
  ""saves.m224"": ""備份："",
  ""saves.m225"": ""損壞檢測："",
  ""saves.m226"": ""提取種子"",
  ""saves.m227"": ""備份"",
  ""saves.m228"": ""降級"",
  ""saves.m229"": ""回滾"",
  ""screenshot.m230"": ""打包分享"",
  ""server.m231"": ""容量上限"",
  ""server.m232"": ""超過該上限後按 LRU 自動淘汰最舊的"",
  ""server.m233"": ""導出到文件夾"",
  ""server.m234"": ""打開緩存目錄"",
  ""server.m235"": ""清空"",
  ""server.m236"": ""共 "",
  ""server.m237"": "" 份緩存，佔用 "",
  ""server.m238"": "" MB（累計命中 "",
  ""server.m239"": "" 次），容量上限 "",
  ""server.m240"": ""文件名"",
  ""server.m241"": ""來源服務器"",
  ""server.m242"": ""命中"",
  ""server.m243"": ""最後使用"",
  ""server.m244"": ""還沒有緩存任何服務器資源包。進服時如果服務器下發了資源包，會自動出現在這裡。"",
  ""server.m245"": ""導出到資源包目錄"",
  ""shader.m246"": ""光影配置參數"",
  ""shader.m247"": ""+ 添加參數"",
  ""shader.m248"": ""參數ID（如shadowRes）"",
  ""shader.m249"": ""參數值（如2048）"",
  ""shader.m250"": ""完整 Token"",
  ""shader.m251"": ""導入 Token"",
  ""shader.m252"": ""從 Token 解析"",
  ""shader.m253"": ""載入"",
  ""shortcut.m254"": ""創建到桌面"",
  ""skin.m255"": ""身體部位"",
  ""skin.m256"": ""工具"",
  ""skin.m257"": ""對稱繪製"",
  ""skin.m258"": ""橡皮擦"",
  ""skin.m259"": ""畫筆大小"",
  ""skin.m260"": ""← 撤銷"",
  ""skin.m261"": ""重做 →"",
  ""skin.m262"": ""清空全部"",
  ""skin.m263"": ""導入 PNG"",
  ""skin.m264"": ""導出 PNG"",
  ""skin.m265"": ""應用到離線賬號"",
  ""skin.m266"": ""2D 編輯"",
  ""skin.m267"": ""3D 預覽"",
  ""skin.m268"": ""纖細手臂"",
  ""skin.m269"": ""縮放"",
  ""skin.m270"": ""填充"",
  ""skin.m271"": ""64x64 預覽"",
  ""skin.m272"": ""當前顏色"",
  ""skin.m273"": ""調色板"",
  ""skin.m274"": ""玩家名："",
  ""skin.m275"": ""查詢"",
  ""skin.m276"": ""拖動旋轉 · 滾輪縮放"",
  ""skin.m277"": ""輸入 Minecraft 正版用戶名以獲取皮膚"",
  ""skin.m304"": ""取色器（含完整色板）"",
  ""skin.m305"": ""副色 ← 主色"",
  ""ver.m278"": ""← 返回"",
  ""ver.m279"": ""返回上一頁"",
  ""ver.m280"": ""已安裝版本"",
  ""ver.m281"": ""版本設置…"",
  ""ver.search"": ""檢索"",
  ""ver.filter_all"": ""全部"",
  ""ver.filter_loader"": ""載入器"",
  ""ver.filter_dir"": ""目錄"",
  ""ver.filter_content"": ""內容"",
  ""ver.filter_missing"": ""僅缺前置"",
  ""ver.filter_reset"": ""清空檢索"",
  ""ver.iso"": ""隔離"",
  ""ver.shared"": ""共享"",
  ""ver.content_mods"": ""Mod"",
  ""ver.content_rp"": ""材質包"",
  ""ver.content_sp"": ""光影"",
  ""ver.content_dp"": ""資料包"",
  ""ver.content_saves"": ""存檔"",
  ""ver.loader_quilt"": ""Quilt"",
  ""ver.loader_neoforge"": ""NeoForge"",
  ""version.m282"": ""① 基礎信息"",
  ""version.m283"": ""刪除該版本"",
  ""version.m284"": ""版本 Id："",
  ""version.m285"": ""  ｜  類型："",
  ""version.m286"": ""  ｜  基版本："",
  ""version.m287"": ""③ 模組加載器"",
  ""version.m288"": ""當前："",
  ""version.m289"": ""加載器會安裝為新實例（基於上方基版本），請在版本列表切換到它。"",
  ""version.m290"": ""④ 隔離與工作目錄"",
  ""version.m291"": ""有效工作目錄："",
  ""version.m292"": ""⑤ Java 與性能"",
  ""version.m293"": ""額外 JVM 參數（每行一個，追加在全局參數之後）"",
  ""version.jvm_presets"": ""常用參數（勾選即可）"",
  ""version.jvm_custom"": ""自訂參數（每行一條）"",
  ""version.jvm_preview"": ""最終生效參數（唯讀預覽）"",
  ""version.jvm_g1gc"": ""啟用 G1 垃圾回收器"",
  ""version.jvm_g1gc_tip"": ""適合大記憶體，停頓更可控（Java 8+ 建議）"",
  ""version.jvm_strdedup"": ""字串去重"",
  ""version.jvm_strdedup_tip"": ""需配合 G1 使用，減少重複字串佔用的記憶體"",
  ""version.jvm_dis_explicit"": ""停用顯式 GC"",
  ""version.jvm_dis_explicit_tip"": ""忽略 System.gc() 呼叫，避免 Mod 觸發卡頓"",
  ""version.jvm_pretouch"": ""啟動時預先配置記憶體"",
  ""version.jvm_pretouch_tip"": ""犧牲一點啟動時間換取執行時更穩定的記憶體配置"",
  ""version.jvm_parref"": ""平行參考處理"",
  ""version.jvm_parref_tip"": ""縮短 GC 中參考處理階段的耗時"",
  ""version.jvm_log4j"": ""關閉 Log4j2 訊息查找"",
  ""version.jvm_log4j_tip"": ""遮蔽 Log4j2 漏洞利用路徑（舊版本建議）"",
  ""version.jvm_fml_cert"": ""Forge：忽略無效憑證"",
  ""version.jvm_fml_cert_tip"": ""修復部分 Forge 版本因憑證校驗失敗無法啟動"",
  ""version.jvm_fml_patch"": ""Forge：忽略修補差異"",
  ""version.jvm_fml_patch_tip"": ""修復 Forge 修補校驗不一致導致的啟動失敗"",
  ""version.jvm_encoding"": ""強制 UTF-8 編碼"",
  ""version.jvm_encoding_tip"": ""避免中文路徑 / 聊天內容亂碼"",
  ""version.jvm_maxpause"": ""GC 最大停頓目標（毫秒）"",
  ""version.jvm_maxpause_tip"": ""G1 會盡量把單次 GC 停頓控制在該值內"",
  ""version.jvm_gcthreads"": ""GC 平行執行緒數"",
  ""version.jvm_gcthreads_tip"": ""一般不超過 CPU 核心數"",
  ""version.jvm_ihop"": ""並發 GC 觸發佔用率（%）"",
  ""version.jvm_ihop_tip"": ""堆佔用達到該百分比時啟動並發標記"",
  ""version.m294"": ""⑥ 分辨率與窗口"",
  ""version.m295"": ""寬"",
  ""version.m296"": ""高"",
  ""version.m297"": ""⑦ 模組與資源包管理"",
  ""version.m298"": ""搜索 Modrinth"",
  ""version.m299"": ""⑧ 版本鎖定"",
  ""version.m300"": ""鎖定該版本（阻止自動更新覆蓋，並阻止安裝加載器 / 增刪 Mod）"",
  ""version.m301"": ""⑨ 賬號綁定"",
  ""version.m302"": ""綁定賬號"",
  ""version.m303"": ""清除綁定"",
  ""install.start"": ""開始安裝"",
  ""install.cancel"": ""取消安裝"",
  ""install.progress"": ""安裝進度"",
  ""install.refresh"": ""重新載入"",
  ""install.done"": ""安裝完成，版本列表已刷新"",
  ""install.cancelled"": ""已取消安裝"",
  ""install.failed"": ""安裝失敗"",
  ""map.zip_hint"": ""地圖 zip 會自動安裝到 saves/，並檢測根目錄前綴。"",
  ""shortcut.title"": ""版本快捷方式"",
  ""shortcut.display_name"": ""顯示名稱（可選）"",
  ""shortcut.tip"": ""提示：雙擊快捷方式即以該版本啟動遊戲（向啟動器傳入 --launch <版本>）。"",
  ""ai.ollama_pre"": ""Ollama 已安裝（"",
  ""ai.ollama_post"": ""）"",
  ""annual.month"": ""月"",
  ""dl.total_pre"": ""共 "",
  ""dl.total_post"": "" 項"",
  ""home.minutes"": "" 分鐘"",
  ""rec.downloads"": "" 下載"",
  ""skin.model_pre"": ""模型："",
  ""achieve.title"": ""成就"",
  ""nat.label"": ""ネットワーク種別"",
  ""nat.probing"": ""測定中…"",
  ""nat.not_probed"": ""未測定"",
  ""nat.eim"": ""コーン NAT — 相手から直接接続できます"",
  ""nat.ad"": ""宛先依存 NAT — 直接接続は成功率が低め"",
  ""nat.apd"": ""対称型 NAT — 直接接続はほぼ不可能"",
  ""nat.unknown"": ""判定できませんでした（一度試す価値はあります）"",
  ""nat.udp_blocked"": ""UDP が通っていません — STUN サーバーが全て無応答"",
  ""nat.stun"": ""STUN サーバー"",
  ""nat.stun_tip"": ""空欄なら自動選択します。学内・社内ネットワークでは指定が必要なことが多いです。host:3478 の形式で、複数ある場合はカンマ区切り"",
  ""nat.fallback_tip"": ""直接接続が駄目なら: 両方の PC を同じ VPN（ZeroTier、Radmin VPN など）に参加させ、上の「LAN 連携」を使ってください。NAT 貫通は不要になります。"",
  ""achieve.purple"": ""紫色成就：{0}"",
  ""afk.add_action"": ""+ 添加動作"",
  ""afk.action_type"": ""動作類型"",
  ""afk.param"": ""參數"",
  ""afk.plaintext_tip"": ""明文文本（將自動轉為 base64）"",
  ""afk.run"": ""運行"",
  ""afk.stop"": ""停止"",
  ""afk.token"": ""令牌"",
  ""afk.save_workflow"": ""保存工作流"",
  ""afk.import_token"": ""導入令牌"",
  ""afk.import_from_token"": ""從令牌導入"",
  ""afk.saved_workflows"": ""已保存工作流"",
  ""afk.load"": ""載入"",
  ""afk.delete_workflow_tip"": ""刪除此工作流"",
  ""afk.picker_title"": ""選擇動作類型"",
  ""afk.picker_hint"": ""點擊一種動作以添加到工作流"",
  ""afk.type_f"": ""功能鍵"",
  ""afk.type_d"": ""延時"",
  ""afk.type_l"": ""長按"",
  ""afk.type_k"": ""虛擬鍵"",
  ""afk.type_c"": ""左鍵連點"",
  ""afk.type_r"": ""右鍵連點"",
  ""p2p.title"": ""インターネット直接接続"",
  ""p2p.section_tip"": ""同じ LAN にいないときはこちら。ハンドシェイクコードを一度交換してから NAT を貫通し、直接通信します。サーバーは一切介在しません。"",
  ""p2p.host_btn"": ""① ワールドを開く側 — 招待コードを作る"",
  ""p2p.join_btn"": ""② 招待コードを受け取った"",
  ""p2p.host_btn_tip"": ""先に Minecraft で LAN に公開しておいてください（上の「ポートを読み取る」を実行）"",
  ""p2p.join_btn_tip"": ""相手から届いた CHERT2: から始まる文字列を貼り付けます"",
  ""p2p.my_ip"": ""自分のグローバル IP"",
  ""p2p.my_ip_tip"": ""分からなければ空欄で構いません。ルーターが UPnP 非対応かつ IPv6 も無い場合のみ必要です"",
  ""p2p.my_code"": ""自分のコード（相手へ送る）"",
  ""p2p.copy_code"": ""コピー"",
  ""p2p.qr_tip"": ""スマホで読み取ってもらうのが最短です。下の文字列を送っても同じです"",
  ""p2p.fingerprint"": ""確認用フィンガープリント"",
  ""p2p.fingerprint_tip"": ""両者で同じ数字か確認してください。違う場合は別の相手に繋がっている可能性があります"",
  ""p2p.peer_code"": ""相手のコード"",
  ""p2p.peer_code_host_tip"": ""相手が返してきた「応答コード」を貼り付けます"",
  ""p2p.peer_code_join_tip"": ""ホストから届いた「招待コード」を貼り付けます"",
  ""p2p.connect"": ""接続開始"",
  ""p2p.stop"": ""切断してリセット"",
  ""p2p.reconnect"": ""再接続"",
  ""p2p.reconnect_tip"": ""交換済みのコードでもう一度穴を開けます。両方で押してください — 片方だけでは NAT が通しません。"",
  ""p2p.role_host"": ""ホスト"",
  ""p2p.role_joiner"": ""ゲスト"",
  ""p2p.ready_joiner"": ""接続できました。Minecraft の「マルチプレイ → 直接接続」に {0} と入力してください"",
  ""p2p.ready_host"": ""トンネル準備完了、相手の参加を待っています"",
  ""p2p.ready_hint"": ""接続できました。Minecraft の「マルチプレイ → 直接接続」に {0} と入力してください"",
  ""p2p.need_local_port"": ""先に上の「ポートを読み取る」を実行してください（転送先ポートが分かりません）"",
  ""p2p.need_choice"": ""まずホスト側かゲスト側かを選んでください"",
  ""p2p.no_code"": ""まだコードがありません"",
  ""p2p.code_copied"": ""コピーしました。相手に送ってください"",
  ""p2p.unsupported"": ""この環境では直接接続を使えません（QUIC は Windows 11 以降、X25519 も必要です）"",
  ""afk.type_m"": ""鼠標移動"",
  ""afk.type_g"": ""按住"",
  ""afk.type_j"": ""隨機等待"",
  ""afk.type_s"": ""滾輪"",
  ""afk.type_t"": ""輸入文本"",
  ""afk.type_u"": ""鬆開"",
  ""afk.type_loop"": ""循環"",
  ""afk.type_e"": ""按鍵"",
  ""cmd.copy_clipboard"": ""複製命令"",
  ""server.name"": ""服務器名稱"",
  ""server.address"": ""服務器地址"",
  ""server.name_empty"": ""服務器名稱不能為空"",
  ""server.addr_empty"": ""服務器地址不能為空"",
  ""settings.theme"": ""主題"",
  ""settings.isolation_tip"": ""新建版本時自動套用的隔離模式：共享=同一 .minecraft；隔離=各自 versions/<id>"",
  ""settings.ms_client_id_note"": ""（留空使用內置默認 client_id）"",
  ""settings.save_settings"": ""保存設置"",
  ""settings.font_scaling"": ""字體縮放："",
  ""mods.mgmt_title"": ""Mod 管理"",
  ""mods.missing_dep_label"": ""缺失依賴："",
  ""mods.required_tag"": ""[必需]"",
  ""mods.conflict_label"": ""衝突："",
  ""mods.installed_tag"": "" 已安裝 "",
  ""mods.conflict_range_tag"": "" 衝突範圍 "",
  ""mods.col_name"": ""名稱"",
  ""mods.col_modid"": ""Mod ID"",
  ""mods.col_version"": ""版本"",
  ""mods.col_loader"": ""加載器"",
  ""mods.col_latest"": ""最新"",
  ""mods.select_label"": ""選擇："",
  ""mods.select_all"": ""全選"",
  ""mods.select_none"": ""全不選"",
  ""mods.select_outdated"": ""選有更新"",
  ""mods.selected_count"": ""已選 "",
  ""mods.verify_selected"": ""核驗勾選"",
  ""mods.verify_all"": ""核驗全部"",
  ""mods.update_selected"": ""更新勾選"",
  ""mods.update_all"": ""更新全部"",
  ""mods.update_one"": ""更新"",
  ""mods.enable"": ""啟用"",
  ""mods.disable"": ""停用"",
  ""mods.toggle"": ""啟/禁"",
  ""mods.uninstall_selected"": ""卸載勾選"",
  ""mods.col_state"": ""狀態"",
  ""mods.col_verify"": ""核驗結果"",
  ""mods.col_action"": ""操作"",
  ""mods.uninstall"": ""卸載"",
  ""seasonal.midautumn.greeting.title"": ""中秋快樂"",
  ""seasonal.midautumn.greeting.message"": ""願你月圓人團圓，遊戲順利～（可在設定中關閉節日特效）"",
  ""seasonal.midautumn.name"": ""中秋節"",
  ""seasonal.midautumn.splash.title"": ""中秋快樂"",
  ""seasonal.midautumn.splash.message"": ""願此夜月色常伴你的每一次冒險"",
  ""seasonal.hub_title"": ""節日中心"",
  ""seasonal.easter_egg"": ""🥚 彩蛋小遊戲"",
  ""seasonal.easter_egg_tip"": ""抓假方塊：它冒出來就點它，點空扣分 —— 全年還能用方向鍵秘技召喚"",
  ""tool.lanlink"": ""區域網路連動"",
  ""lan.title"": ""區域網路連動"",
  ""lan.enable"": ""允許被區域網路發現"",
  ""lan.enable_tip"": ""開啟後本機監聽發現 / 指令埠，同一 Wi-Fi 下的另一台燧石就能找到你"",
  ""lan.service_tip"": ""首次監聽會彈 Windows 防火牆提示，點「允許存取」即可；不需要管理員權限。關閉開關即停止監聽。"",
  ""lan.my_world"": ""我的世界"",
  ""lan.publish"": ""一鍵開放"",
  ""lan.publish_tip"": ""向遊戲視窗投遞 /publish（等同於遊戲內的「對區域網路開放」）"",
  ""lan.detect"": ""讀取本機埠"",
  ""lan.detect_tip"": ""從遊戲日誌讀取已開放的區域網路埠"",
  ""lan.copy_share"": ""複製邀請"",
  ""lan.devices"": ""區域網路裝置"",
  ""lan.manual_ip"": ""對方 IP"",
  ""lan.manual_ip_tip"": ""廣播被禁用時可手動填對方區域網路 IP 定向探測"",
  ""lan.probe"": ""探測"",
  ""lan.pair"": ""配對"",
  ""lan.gen_code"": ""產生配對碼"",
  ""lan.input_code"": ""對方配對碼"",
  ""lan.do_pair"": ""配對"",
  ""lan.pair_tip"": ""雙方各產生一次配對碼、互相輸入即可建立會話；配對碼 10 分鐘有效。未配對的裝置發來的指令一律拒絕。"",
  ""lan.actions"": ""連動"",
  ""lan.invite"": ""邀請加入我的世界"",
  ""lan.invite_tip"": ""把本機世界位址發給選中裝置，對方螢幕上會彈確認框"",
  ""lan.request_open"": ""請對方開放世界"",
  ""lan.request_open_tip"": ""請求對方的 Minecraft 執行 /publish"",
  ""lan.join_addr"": ""世界位址"",
  ""lan.join"": ""加入"",
  ""lan.join_tip"": ""CHERT1: 招待コード、または 192.168.1.5:52931 形式の住所を貼り付けられます"",
  ""lan.invite_title"": ""區域網路連線邀請"",
  ""lan.gen_invite"": ""招待コード生成"",
  ""lan.gen_invite_tip"": ""住所・端末名・ペアコードを 1 つの文字列にまとめます"",
  ""lan.copy_invite"": ""コードをコピー"",
  ""lan.copy_invite_tip"": ""CHERT1: から始まる全文をコピーします"",
  ""lan.copy_code"": ""コピー"",
  ""lan.code_none"": ""まだペアコードがありません。先に生成してください"",
  ""lan.code_copied"": ""ペアコードをコピーしました"",
  ""lan.check"": ""確認コード"",
  ""lan.invite_valid"": ""{0} 分間有効"",
  ""lan.invite_ready"": ""招待コードを生成しました。全文をコピーして相手に送ってください"",
  ""lan.invite_copied"": ""招待コードをコピーしました"",
  ""lan.invite_need_endpoint"": ""先に「ローカルポートを読む」で住所を取得してください"",
  ""lan.invite_stale"": ""ワールド住所が変わったので招待コードを再生成してください"",
  ""lan.invite_from"": ""{0} からの招待・確認コード {1}"",
  ""lan.err_expired"": ""コードの有効期限（10 分）が切れています。再生成してもらってください"",
  ""lan.err_broken"": ""コードが途中で切れているか書き換えられています。全文を再コピーしてください"",
  ""lan.err_version"": ""対応していないバージョンのコードです。両方のランチャーを更新してください"",
  ""lan.err_endpoint"": ""アドレスの形式が違います（例 192.168.1.5:52931）"",
  ""lan.err_endpoint_fmt"": ""アドレスの形式が違います：{0}"",
  ""lan.err_generic"": ""コードを判別できません。全文がコピーされているか確認してください"",
  ""lan.detect_scanning"": ""ログにポートがありません。3 秒間 LAN ブロードキャストを監視します…"",
  ""lan.detect_fail"": ""ポートを取得できません。ゲーム内で「LAN に公開」してください。"",
  ""lan.detect_ok"": ""ローカルワールドの住所"",
  ""lan.copy_none"": ""まだ共有できる住所がありません"",
  ""lan.copy_done"": ""招待文をコピーしました"",
  ""lan.copy_fail"": ""コピーに失敗しました。手動で選択してコピーしてください"",
  ""lan.join_need_addr"": ""ワールド住所を入力するか招待コードを貼り付けてください"",
  ""lan.joining"": ""参加中"",
  ""seasonal.hub_loading"": ""節日設定尚未就緒，請稍後重新整理"",
  ""seasonal.hub_empty"": ""目前沒有可顯示的節日內容"",
  ""seasonal.hub_summary"": ""活動 {0} 項 · 伺服器 {1} 個 · 推薦 {2} 個"",
  ""seasonal.current"": ""目前節日"",
  ""seasonal.none"": ""目前不在任何節日檔期"",
  ""seasonal.events_title"": ""限時活動"",
  ""seasonal.servers_title"": ""節日伺服器推薦"",
  ""seasonal.picks_title"": ""節日內容推薦"",
  ""seasonal.events_empty"": ""暫無期間限定活動"",
  ""seasonal.servers_empty"": ""暫無節日伺服器推薦"",
  ""seasonal.picks_empty"": ""暫無節日內容推薦"",
  ""seasonal.open_link"": ""開啟"",
  ""seasonal.copy_address"": ""複製位址"",
  ""seasonal.copied"": ""已複製伺服器位址"",
  ""seasonal.copy_failed"": ""複製失敗"",
  ""seasonal.announcement.title"": ""節日公告"",
  ""seasonal.kind.mod"": ""模組"",
  ""seasonal.kind.modpack"": ""整合包"",
  ""seasonal.kind.resourcepack"": ""材質包"",
  ""seasonal.kind.shader"": ""光影"",
  ""seasonal.kind.version"": ""版本"",
  ""seasonal.kind.other"": ""其他"",
  ""seasonal.effects_hint"": ""節日特效與內容由遠端設定驅動，可在「設定 → 外觀」中關閉節日特效。"",
  ""settings.seasonal_effects"": ""節日特效"",
  ""settings.java_path_hint"": ""此項僅作為偏好 Java；啟動器仍會依各版本需求自動避開其不支援的 Java 版本，無須手動切換。"",
  ""settings.use_littleskin"": ""使用 LittleSkin"",
  ""settings.use_littleskin_tip"": ""自動填入 LittleSkin 的外置登入伺服器位址"",
  ""settings.authlib_url_ph"": ""伺服器位址，如 https://auth.example.com"",
  ""settings.authlib_email_ph"": ""該伺服器的帳號郵箱""
}";
}
