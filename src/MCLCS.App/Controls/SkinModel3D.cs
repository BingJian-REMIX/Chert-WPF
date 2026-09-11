using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace MCLCS.App.Controls;

/// <summary>
/// 根据 Minecraft 皮肤位图构建方块角色 3D 模型。
/// <para>
/// 实现策略：把皮肤 PNG 按面裁切为独立小图，每面一个 DiffuseMaterial + Quad，
/// 避免整张皮肤走 UV 图时出现的采样漂移、左右面混淆、slim 手臂面宽不一致等问题。
/// 支持标准 64×64（Java 1.8+）与 64×32 旧皮肤，以及 slim/classic 手臂。
/// </para>
/// </summary>
public static class SkinModel3D
{
    private const double CenterY = 16.0; // 身高 32，平移使垂直中心落在原点

    /// <summary>一个皮肤矩形（像素：X, Y, W, H）。</summary>
    private readonly record struct Rect(int X, int Y, int W, int H);

    /// <summary>一个身体部位 6 个面的皮肤矩形。顺序：Front, Back, Left, Right, Top, Bottom。</summary>
    private readonly record struct Uv(Rect Front, Rect Back, Rect Left, Rect Right, Rect Top, Rect Bottom);

    /// <summary>一个身体部位的定义（尺寸、中心、6 面矩形、第二层矩形）。</summary>
    private readonly record struct PartDef(
        Uv First, Uv Overlay,        // 头/躯干使用（IsLimb=false）
        int Fx, int Fy, int Ofx, int Ofy, int MirrorFx, int MirrorFy, // 四肢正面坐标(实体/第二层) 与 legacy 镜像源(右肢)；非四肢置 0
        double W, double H, double D, double Cx, double Cy, bool IsArm, bool IsLimb);

    /// <summary>第一层（实体）部位：头/躯干/右臂/左臂/右腿/左腿。</summary>
    private static readonly PartDef[] FirstLayer =
    {
        new(HeadUv(),  HatUv(),  0,0,0,0,0,0,  8, 8,  8,  0, 28, false, false),  // 头
        new(BodyUv(),  JacketUv(),0,0,0,0,0,0,  8, 12, 4,  0, 18, false, false),  // 躯干
        new(default, default, 44,20,44,36,44,20, 4, 12, 4,  6, 18, true,  true),  // 右臂
        new(default, default, 36,52,52,52,44,20, 4, 12, 4, -6, 18, true,  true),  // 左臂
        new(default, default,  4,20, 4,36, 4,20, 4, 12, 4,  2,  6, false, true),  // 右腿
        new(default, default, 20,52, 4,52, 4,20, 4, 12, 4, -2,  6, false, true),  // 左腿
    };

    /// <summary>构建角色模型。</summary>
    public static Model3DGroup Build(BitmapSource skin, bool slim)
    {
        var group = new Model3DGroup();

        bool legacy = skin.PixelHeight is <= 32;
        int tw = Math.Max(1, skin.PixelWidth);
        int th = Math.Max(1, skin.PixelHeight);

        foreach (var def in FirstLayer)
        {
            BuildPart(group, skin, tw, th, def, slim, legacy, useOverlay: false);
            if (!legacy)
                BuildPart(group, skin, tw, th, def, slim, legacy, useOverlay: true);
        }

        return group;
    }

    private static void BuildPart(Model3DGroup group, BitmapSource skin, int tw, int th, PartDef def, bool slim, bool legacy, bool useOverlay)
    {
        double w = def.IsArm && slim ? 3 : def.W;
        double h = def.H;
        double d = def.D;

        // 原版正面约定：角色右手在观察者左侧(-X)，左手在观察者右侧(+X)。
        // 原表中右臂 Cx=6（错在右侧），这里取反纠正。
        double cx = -def.Cx;
        if (def.IsArm && slim) cx -= Math.Sign(cx) * 0.5; // slim 手臂向身体中心收 0.5

        double cy = def.Cy - CenterY;

        // 计算 6 面 UV：
        //  - 四肢按有效宽度（slim=3 / classic=4）由 LimbUv 统一推导，保证 front/back/top/bottom
        //    收窄为 w 的同时，left/back/bottom 的 X 同步左移，避免 slim 手臂后方/下方采样错位（缺一半）。
        //  - 头/躯干用固定 UV。
        Uv uv;
        if (def.IsLimb)
        {
            int lw = def.IsArm && slim ? 3 : 4;
            uv = useOverlay ? LimbUv(def.Ofx, def.Ofy, lw) : LimbUv(def.Fx, def.Fy, lw);
        }
        else
        {
            uv = useOverlay ? def.Overlay : def.First;
        }

        // 64×32 旧皮肤无独立左侧区域：左臂/左腿镜像复用右侧纹理。
        // 必须左右镜像（交换 Left/Right），否则左肢外侧会显示右肢内侧纹理，
        // 而旧皮肤右肢内侧多数为空 → 左肢外侧缺贴图。注意此分支仅限 legacy 且非第二层。
        if (legacy && def.IsLimb && !useOverlay && def.Cx < 0)
        {
            int lw = def.IsArm && slim ? 3 : 4;
            var baseUv = LimbUv(def.MirrorFx, def.MirrorFy, lw);
            uv = baseUv with { Left = baseUv.Right, Right = baseUv.Left };
        }

        // 面 ↔ 模型方向约定（AddBox 固定）：
        //   +Z = Front, -Z = Back, -X = Right(角色右手外侧), +X = Left(角色左手外侧),
        //   +Y = Top, -Y = Bottom。
        // 左肢(def.Cx<0)位于身体 +X 侧，其 +X 面即外侧；皮肤 Left 区已是该肢外侧纹理，
        // 故 +X→uv.Left 自然正确，**无需**交换 Left/Right（早期交换反而把内外贴反）。

        double expand = 0.0;
        if (useOverlay)
        {
            expand = def.W is 8 && def.H is 8 ? 1.0 : 0.5; // 帽子 0.5/边；衣裤 0.25/边
            w += expand;
            h += expand;
            d += expand;
        }

        AddBox(group, skin, tw, th, cx, cy, 0, w, h, d, uv);
    }

    // —— UV 构造助手（坐标均来自标准 64×64 布局；w = 4 classic / 3 slim） ——
    // 以右/外侧面(Right)为基准 x=r，顶沿 y=s，推导 6 面：
    //   Right = (r,     s,     4, 12)  外侧
    //   Front = (r+4,   s,     w, 12)  正面（front 紧贴外侧，宽 w）
    //   Left  = (r+4+w, s,     4, 12)  内侧（深度面，宽恒 4）
    //   Back  = (r+4+w+4, s,   w, 12)  背面（宽 w）
    //   Top   = (r+4,   s-4,   w, 4)   顶面（宽 w）
    //   Bottom= (r+4+w, s-4,   w, 4)   底面（宽 w）
    // 其中 r = fx-4, s = fy（fx/fy 为正面左上角）。展开即下式。
    private static Uv LimbUv(int fx, int fy, int w)
    {
        int right = fx - 4;          // 外侧(Right)左沿
        int top = fy;               // 侧面顶沿（与正面顶沿同高）
        return new Uv(
            new Rect(fx,        fy,     w, 12),  // Front
            new Rect(fx + w + 4, fy,   w, 12),  // Back
            new Rect(fx + w,    fy,    4, 12),  // Left
            new Rect(right,     fy,    4, 12),  // Right
            new Rect(fx,        fy - 4, w, 4),  // Top
            new Rect(fx + w,    fy - 4, w, 4)); // Bottom
    }

    private static Uv HeadUv() => new(
        new Rect(8, 8, 8, 8), new Rect(24, 8, 8, 8), new Rect(16, 8, 8, 8), new Rect(0, 8, 8, 8),
        new Rect(8, 0, 8, 8), new Rect(16, 0, 8, 8));

    private static Uv BodyUv() => new(
        new Rect(20, 20, 8, 12), new Rect(32, 20, 8, 12), new Rect(16, 20, 4, 12), new Rect(28, 20, 4, 12),
        new Rect(20, 16, 8, 4), new Rect(28, 16, 8, 4));

    private static Uv HatUv() => new(
        new Rect(40, 8, 8, 8), new Rect(56, 8, 8, 8), new Rect(48, 8, 8, 8), new Rect(32, 8, 8, 8),
        new Rect(40, 0, 8, 8), new Rect(48, 0, 8, 8));

    private static Uv JacketUv() => new(
        new Rect(20, 36, 8, 12), new Rect(32, 36, 8, 12), new Rect(16, 36, 4, 12), new Rect(28, 36, 4, 12),
        new Rect(20, 32, 8, 4), new Rect(28, 32, 8, 4));

    private static void AddBox(Model3DGroup group, BitmapSource skin, int tw, int th,
        double cx, double cy, double cz, double w, double h, double d, Uv uv)
    {
        double hx = w / 2, hy = h / 2, hz = d / 2;
        Point3D P(double x, double y, double z) => new Point3D(cx + x, cy + y, cz + z);

        // 各面四角（外视 CCW：左上、右上、右下、左下）
        AddFace(group, skin, tw, th,
            P(-hx, +hy, +hz), P(+hx, +hy, +hz), P(+hx, -hy, +hz), P(-hx, -hy, +hz), uv.Front);   // 正面 +Z
        AddFace(group, skin, tw, th,
            P(+hx, +hy, -hz), P(-hx, +hy, -hz), P(-hx, -hy, -hz), P(+hx, -hy, -hz), uv.Back);    // 背面 -Z
        AddFace(group, skin, tw, th,
            P(-hx, +hy, -hz), P(-hx, +hy, +hz), P(-hx, -hy, +hz), P(-hx, -hy, -hz), uv.Right);   // 左面 -X：角色右手外侧 → 皮肤 Right 区
        AddFace(group, skin, tw, th,
            P(+hx, +hy, +hz), P(+hx, +hy, -hz), P(+hx, -hy, -hz), P(+hx, -hy, +hz), uv.Left);    // 右面 +X：角色左手外侧 → 皮肤 Left 区
        AddFace(group, skin, tw, th,
            P(-hx, +hy, -hz), P(+hx, +hy, -hz), P(+hx, +hy, +hz), P(-hx, +hy, +hz), uv.Top);      // 顶面 +Y
        // 底面 -Y：底面法线指向盒内(+Y)，外部(-Y)观察看到的是背面材质，纹理实测呈 180° 旋转
        // （头底/手掌尤为明显）。此处把四角顺序整体反转，使纹理整体旋转 180° 修正；
        // 仅重排「顶点 ↔ UV」映射，不改变四边形几何，配合双面材质不会丢失面。
        AddFace(group, skin, tw, th,
            P(-hx, -hy, -hz), P(+hx, -hy, -hz), P(+hx, -hy, +hz), P(-hx, -hy, +hz), uv.Bottom);   // 底面 -Y（纹理 180° 修正）
    }

    private static void AddFace(Model3DGroup group, BitmapSource skin, int tw, int th,
        Point3D tl, Point3D tr, Point3D br, Point3D bl, Rect r)
    {
        // 把矩形裁到贴图边界内，防止 slim/legacy 算出的区域越界。
        int x = Math.Max(0, Math.Min(tw - 1, r.X));
        int y = Math.Max(0, Math.Min(th - 1, r.Y));
        int w = Math.Max(0, Math.Min(tw - x, r.W));
        int h = Math.Max(0, Math.Min(th - y, r.H));
        if (w <= 0 || h <= 0) return;

        var cropped = new CroppedBitmap(skin, new Int32Rect(x, y, w, h));
        if (cropped.CanFreeze) cropped.Freeze();

        // 像素艺术 3D 的坑：
        //  1) 非 2 的幂(NPOT)纹理在 WPF 3D(Direct3D9)下会渲染异常甚至整面缺失。
        //  2) WPF 3D 对 Material 纹理采样默认走双线性过滤，若把 4×12 小图直接贴到
        //     屏幕上百像素的面，放大时会被糊成一片。
        //  3) 全图预放大 32× 又会导致缩小绘制时触发 mipmap 三线性插值，同样发糊。
        // 修法：把每面按整数倍（8×）最近邻放大，再补到 2 的幂画布。这样纹理分辨率
        // 与屏幕像素接近，即便 WPF 3D 内部用线性采样也不会出现明显模糊；同时 POT
        // 保证不会缺面。UV 按内容区 (sw/pw)×(sh/ph) 收窄，避免透明留边被拉伸到面上。
        const int Scale = 8;
        int sw = w * Scale;
        int sh = h * Scale;
        int pw = NextPow2(sw);
        int ph = NextPow2(sh);

        int bpp = (cropped.Format.BitsPerPixel + 7) / 8;
        int srcStride = w * bpp;
        byte[] srcPixels = new byte[h * srcStride];
        cropped.CopyPixels(srcPixels, srcStride, 0);

        int dstStride = pw * bpp;
        byte[] dstPixels = new byte[ph * dstStride];
        for (int dy = 0; dy < sh; dy++)
        {
            int sy = dy / Scale;
            int srcRow = sy * srcStride;
            int dstRow = dy * dstStride;
            for (int dx = 0; dx < sw; dx++)
            {
                int sx = dx / Scale;
                int srcIdx = srcRow + sx * bpp;
                int dstIdx = dstRow + dx * bpp;
                for (int b = 0; b < bpp; b++)
                    dstPixels[dstIdx + b] = srcPixels[srcIdx + b];
            }
        }

        // 边缘钳制填充：把 POT 透明留边（内容区之外，尤其是 v=vMax 落到的留边首行）用
        // 相邻内容像素填充。否则 WPF 3D 在纹理寻址时采样到透明留边，会让四肢侧面 / 躯干
        // 底边等出现「透明 / 缺面」回归（NPOT 缺面问题在 POT 化后仍会以留边被采样的形式复现）。
        // 钳制后即便越界采样也取到内容色而非透明。
        int lastRow = sh - 1;
        for (int dy = sh; dy < ph; dy++)
            Buffer.BlockCopy(dstPixels, lastRow * dstStride, dstPixels, dy * dstStride, sw * bpp);
        int lastCol = sw - 1;
        for (int dx = sw; dx < pw; dx++)
            for (int dy = 0; dy < ph; dy++)
                Buffer.BlockCopy(dstPixels, dy * dstStride + lastCol * bpp, dstPixels, dy * dstStride + dx * bpp, bpp);

        var tex = new WriteableBitmap(pw, ph, 96, 96, cropped.Format, null);
        tex.WritePixels(new Int32Rect(0, 0, pw, ph), dstPixels, dstStride, 0);
        RenderOptions.SetBitmapScalingMode(tex, BitmapScalingMode.NearestNeighbor);
        if (tex.CanFreeze) tex.Freeze();

        var brush = new ImageBrush(tex)
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None,
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        var material = new DiffuseMaterial(brush);

        // 贴图只占用 POT 画布的 (sw/pw)×(sh/ph) 区域，UV 据此收窄。
        double uMax = (double)sw / pw;
        double vMax = (double)sh / ph;

        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(tl);
        mesh.Positions.Add(tr);
        mesh.Positions.Add(br);
        mesh.Positions.Add(bl);
        mesh.TextureCoordinates.Add(new Point(0, 0));
        mesh.TextureCoordinates.Add(new Point(uMax, 0));
        mesh.TextureCoordinates.Add(new Point(uMax, vMax));
        mesh.TextureCoordinates.Add(new Point(0, vMax));
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(3);

        group.Children.Add(new GeometryModel3D(mesh, material)
        {
            BackMaterial = material // 双面渲染，旋转时背面不缺失
        });
    }

    private static int NextPow2(int n)
    {
        int p = 1;
        while (p < n) p <<= 1;
        return p;
    }
}
