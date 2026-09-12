// 全局 using 别名：解决 UseWPF 隐式 using 与代码同名类型冲突（CS0104）
//   System.IO.Path        vs  System.Windows.Shapes.Path
//   Chert.Core.Toolbox.Track  vs  System.Windows.Controls.Primitives.Track
// 注意：原先放在 csproj 的 <Using Alias> 会被 WPF 的 wpftmp 临时项目以相反顺序生成，
//       导致自动生成的 GlobalUsings.g.cs 出现语法错误；改为代码文件后仅主项目编译生效，
//       wpftmp 不编译主项目 .cs 文件，从而不再生成错误别名。
global using Path = System.IO.Path;
global using Track = Chert.Core.Toolbox.Track;

// 显式补充的全局命名空间：WPF 的 wpftmp 临时项目（MarkupCompile 生成）自带的
// ImplicitUsings 子集不含 System.IO/System.Text 等，会导致主项目 .cs 在该临时项目下
// 编译时找不到 File/Encoding/Process 等类型。这里显式声明以补齐（主项目也会一并生效）。
global using System.IO;
global using System.Text;
global using System.Diagnostics;
global using System.Net.Http;
global using System.Text.Json;
global using System.Text.RegularExpressions;
