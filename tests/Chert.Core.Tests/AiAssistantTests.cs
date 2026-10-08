using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chert.Core.Ai;
using Xunit;

namespace Chert.Core.Tests;

/// <summary>
/// AI 助手的行为约定。这里只测「模型没参与时应该怎么办」——
/// 那部分失败路径最容易被写成「反正返回一段话就行」，结果用户看到的原文被当成译文。
/// </summary>
public class AiAssistantTests : IDisposable
{
    private readonly AiConfig _saved = Assistant.Config;

    public void Dispose() => Assistant.Config = _saved;

    [Fact]
    public async Task 未启用时崩溃解读退回本地启发式且标明不是模型说的()
    {
        Assistant.Config = new AiConfig { Enabled = false };

        var r = await Assistant.InterpretCrashAsync("java.lang.OutOfMemoryError: Java heap space");

        Assert.False(r.FromAi);
        Assert.Contains("内存不足", r.Text);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public async Task 崩溃解读的开关被关掉时会说清楚原因()
    {
        Assistant.Config = new AiConfig { Enabled = true, CrashInterpret = false };

        var r = await Assistant.InterpretCrashAsync("UnsupportedClassVersionError");

        Assert.False(r.FromAi);
        Assert.Contains("崩溃解读", r.Error!);
        // 本地启发式仍然要给出有用的结论
        Assert.Contains("Java", r.Text);
    }

    [Fact]
    public async Task 未启用时翻译原样返回而不是假装翻译好了()
    {
        Assistant.Config = new AiConfig { Enabled = false };

        var r = await Assistant.TranslateModDescriptionAsync("A mod that adds chests.");

        Assert.False(r.FromAi);
        Assert.Equal("A mod that adds chests.", r.Text);
    }

    [Fact]
    public async Task 翻译开关关掉时同样如实标注()
    {
        Assistant.Config = new AiConfig { Enabled = true, ModTranslate = false };

        var r = await Assistant.TranslateModDescriptionAsync("hello");

        Assert.False(r.FromAi);
        Assert.Equal("hello", r.Text);
        Assert.Contains("Mod 描述翻译", r.Error!);
    }

    [Fact]
    public async Task 地址不通时回退而不是抛异常()
    {
        // 127.0.0.1:1 上不会有服务，连接会被立刻拒绝
        Assistant.Config = new AiConfig
        {
            Enabled = true,
            Mode = AiMode.External,
            Endpoint = "http://127.0.0.1:1/v1/chat/completions",
            Model = "whatever"
        };

        var r = await Assistant.TranslateModDescriptionAsync("hello");

        Assert.False(r.FromAi);
        Assert.Equal("hello", r.Text);
    }

    [Fact]
    public async Task 端点为空时直接回退()
    {
        Assistant.Config = new AiConfig { Enabled = true, Endpoint = "" };

        var r = await Assistant.ChatAsync(new List<AiChatMessage> { AiChatMessage.User("你好") });

        Assert.False(r.FromAi);
    }

    [Fact]
    public async Task 空上下文不发请求()
    {
        Assistant.Config = new AiConfig { Enabled = true };

        var r = await Assistant.ChatAsync(Array.Empty<AiChatMessage>());

        Assert.Equal("", r.Text);
        Assert.False(r.FromAi);
    }

    [Fact]
    public void Key落盘是混淆串且内存里能取回明文()
    {
        var cfg = new AiConfig();
        Assistant.CommitApiKey("sk-test-123456", cfg);

        // 落盘那份不能是明文
        Assert.NotEqual("sk-test-123456", cfg.ApiKey);
        Assert.StartsWith("mclcs1:", cfg.ApiKey!);
        Assert.Equal("sk-test-123456", Assistant.RevealApiKey(cfg));
    }

    [Fact]
    public void 老配置里的明文Key仍然能用()
    {
        // 没有 mclcs1: 前缀 → 解不开 → 按明文原样返回，交给服务端去判
        var cfg = new AiConfig { ApiKey = "sk-legacy-plain" };

        Assert.Equal("sk-legacy-plain", Assistant.RevealApiKey(cfg));
    }

    [Fact]
    public void 清空Key时两边都不留()
    {
        var cfg = new AiConfig();
        Assistant.CommitApiKey("sk-abc", cfg);
        Assistant.CommitApiKey("   ", cfg);

        Assert.Null(cfg.ApiKey);
        Assert.Null(cfg.ApiKeyPlain);
        Assert.Equal("", Assistant.RevealApiKey(cfg));
    }

    [Fact]
    public void 对话消息的三个角色拼出来是对的()
    {
        Assert.Equal("system", AiChatMessage.System("人设").Role);
        Assert.Equal("user", AiChatMessage.User("问题").Role);
        Assert.Equal("assistant", AiChatMessage.Assistant("回答").Role);
        Assert.Equal(AiChatMessage.User("同一个问题"), AiChatMessage.User("同一个问题"));
    }

    [Fact]
    public void 结果的两种形态语义分明()
    {
        var ok = AiResult.Ok("译文本");
        Assert.True(ok.FromAi);
        Assert.Null(ok.Error);

        var fb = AiResult.Fallback("原文", "原因");
        Assert.False(fb.FromAi);
        Assert.Equal("原因", fb.Error);
    }

    [Fact]
    public async Task 推荐与总结在空输入时不出怪东西()
    {
        Assistant.Config = new AiConfig { Enabled = true };

        var rec = await Assistant.RecommendModsAsync("   ");
        Assert.Equal("", rec.Text);

        var sum = await Assistant.SummarizeAsync("");
        Assert.Equal("", sum.Text);
    }
}
