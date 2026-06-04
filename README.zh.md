# Lian Li LCD Theme Editor

**语言:** [English](README.en.md) | [Türkçe](README.tr.md) | [Русский](README.ru.md) | [简体中文](README.zh.md)

这是一个用于 Lian Li L-Connect 3 LCD 模板的非官方 Windows 主题编辑器。

本编辑器面向 Hydroshift II LCD 系列设备。它可以帮助你查看、编辑、添加、调整顺序、预览并应用 LCD 模板图层，而不必在 L-Connect 中手动修改每一个元素。

> 本项目与 Lian Li 没有关联，也不是官方应用。感谢 Lian Li 允许我在官方 Discord 中介绍这个项目，但如果编辑器在你的电脑上无法正常工作，请不要把这个问题提交给 Lian Li 官方支持。该应用会修改本机的 L-Connect 3 模板和配置文件，因此编辑重要主题前请先备份。

## 截图

<img width="2546" height="1370" alt="image" src="https://github.com/user-attachments/assets/3fd774bc-e45a-44eb-822d-a93642ade68a" />

## 功能

- 编辑现有的 L-Connect 3 LCD 模板图层。
- 支持方形屏幕的 Hydroshift II LCD-S 和圆形屏幕的 Hydroshift II LCD-C。
- 当当前模板属于另一种设备系列时，自动使用备用查找逻辑。
- LCD-S 使用方形预览，LCD-C 使用圆形遮罩预览。
- 实时预览文字、数据、图片、图表、GIF 和 MP4 图层。
- 上传并应用 GIF/MP4 作为背景。
- 图层列表可显示并编辑序号、类型、数据源、文字、媒体、位置、大小、字体、粗体、颜色和格式。
- 添加静态文字图层。
- 添加实时数据图层，例如 CPU/GPU 温度、负载、频率、风扇/水泵数据、时间、日期和星期。
- 添加图片图层。
- 从 L-Connect 可用的模块化图表样式中添加图表图层。
- 在图层支持的情况下编辑图表样式、位置、大小、颜色和数据源。
- 上移或下移图层以控制绘制顺序。
- 添加并跟踪阴影图层。
- 将阴影的移动和颜色与源图层同步。
- 通过 ARGB/HEX 手动输入支持透明颜色。
- 支持日期和时间格式，例如 `Y-M-D`、`D-M-Y`、`D.M.Y`、`00:00`、`00:00:00` 以及 AM/PM。
- 多语言界面：英语、土耳其语、俄语和简体中文。
- 深色/浅色界面主题。
- `Apply All` 流程可保存模板更改并让 L-Connect 刷新，而不需要重启可能影响风扇控制的服务。
- 可选的 `ps2exe` EXE 构建支持。

## 支持的设备

| 设备 | 状态 | 说明 |
| --- | --- | --- |
| Hydroshift II LCD-S | 支持 | 方形 LCD 预览。 |
| Hydroshift II LCD-C | 支持 | 圆形预览遮罩，使用圆形屏幕对应的模板和模块化资源。 |

如果可能，编辑器可以从已安装的 L-Connect `Assets` 目录补齐 `ProgramData` 中缺失的模板、模块化元素、主题和预览文件。

## 安装

1. 打开 Release 页面：
   <https://github.com/ozgurce/LianliThemeEditor/releases>
2. 下载 `EXE.zip`。
3. 解压压缩包。
4. 解压后的结构应如下：

```text
ThemeEditor.exe
supporter.exe
lang/
  en.json
  ru.json
  tr.json
  zh.json
```

5. 运行 `ThemeEditor.exe`。

## 系统要求

- Windows 10 或 Windows 11。
- 已安装 L-Connect 3。
- PowerShell 5.1 或更新版本。
- 通过 Windows PowerShell 提供 .NET/WPF 支持。
- 写入 `C:\ProgramData\Lian-Li\L-Connect 3` 时建议使用管理员权限。
- 建议安装 `ffmpeg`，以便更可靠地处理背景视频/GIF 转换和预览。
- 可选：如果你想从 PowerShell 脚本自行构建独立 `.exe` 文件，可以使用 `ps2exe`。

从 `EXE/` 文件夹运行时，编辑器会在 `.exe` 所在位置以及项目父级目录中查找所需资源。

## 通过 PowerShell 运行

打开 PowerShell 并执行：

```powershell
powershell -ExecutionPolicy Bypass -File editor.ps1
```

如果项目放在其他目录，请相应修改 `editor.ps1` 的路径。

## 构建 EXE 文件

安装或导入 `ps2exe`，然后在项目文件夹中运行构建命令。正式发布包中的 `.exe` 文件已经包含在 `EXE.zip` 中，因此本节主要面向想要自行构建应用的用户。

## 基本用法

1. 打开 L-Connect 3，并选择受支持的 LCD 设备。
2. 在 L-Connect 中选择一个模板，或者保留当前活动模板。
3. 运行 `ThemeEditor.exe`。
4. 在编辑器中选择设备类型：
   - `Hydroshift II LCD-S`
   - `Hydroshift II LCD-C`
5. 保持 `Use active template` 开启，或手动输入模板 ID。
6. 点击 `Load`。
7. 在图层列表中选择图层，或直接在预览区域中选择图层。
8. 编辑位置、字体、数据源、文字、大小、颜色、格式或图表选项。
9. 对选中的图层点击 `Apply`。
10. 使用 `Apply All` 写入更改并触发 L-Connect 刷新。

## 图层类型

常见图层类型包括：

- `GraphAnimation`：用于视频、GIF 或图片的背景图层。
- `GraphItem`：文字或数据文字图层。
- `GraphImage`：图片图层。
- `GraphStatuBar`：线性进度/状态条。
- `GraphArchBar`：圆形或弧形图表。
- `GraphLine`：类似任务管理器曲线的流式折线图。
- `GraphDynamicBar`：动态分段图表或条形元素。

并不是每个 L-Connect 图表对象都拥有所有属性。编辑器会根据所选图层支持的属性显示和应用对应控件。

## 数据源

编辑器只保留 L-Connect 模板实际可以使用或显示的实用数据源。

- `CPUTEMP`：CPU 温度。
- `CPUCLOCK`：CPU 频率。
- `CPULOAD`：CPU 负载。
- `CPUFAN`：L-Connect/HWiNFO 识别为 CPU 风扇的转速。在某些系统上该值可能不存在，或名称不同。
- `GPUTEMP`：GPU 温度。
- `GPUCLOCK`：GPU 频率。
- `GPULOAD`：GPU 负载。
- `RAMLOAD`：内存使用率。
- `DRVLOAD`：磁盘使用率。
- `WATERPUMP`：水泵转速。
- `TIME`：当前时间。
- `DATE`：当前日期。
- `DAY`：星期。显示效果取决于 L-Connect 的格式和行为。
- `APM`：12 小时时间格式中的 AM/PM 指示。
- `StaticText`：静态文字。

部分数值取决于硬件、L-Connect 版本以及可用传感器。

## 日期和时间格式

时间格式示例：

```text
00:00
00:00:00
```

日期格式示例：

```text
Y-M-D
D-M-Y
D.M.Y
M
D
```

日期和时间图层应保持动态，不应作为普通静态文字保存。

## 背景媒体

编辑器支持选择 GIF/MP4 作为背景。应用背景时，辅助模块会尽量模拟 L-Connect 保存已上传背景媒体的方式。

常用路径：

```text
C:\ProgramData\Lian-Li\L-Connect 3\uploaded
C:\ProgramData\Lian-Li\L-Connect 3\hydroshift-ii-lcd-s
C:\ProgramData\Lian-Li\L-Connect 3\hydroshift-ii-lcd-c
```

编辑器在普通应用操作中会避免重启 L-Connect 服务，因为该服务也可能负责风扇和水泵控制。

## 语言支持

语言文件位于：

```text
lang/en.json
lang/tr.json
lang/ru.json
lang/zh.json
```

如果某个界面字符串缺失翻译，或仍然像是直接写在代码中，应将它添加到所有 JSON 语言文件，并通过 `editor.ps1` 中的本地化辅助逻辑连接。

## 设置

本地编辑器设置保存在：

```text
theme_editor_settings.json
```

它可能包含：

- 选择的语言；
- 选择的界面主题；
- 选择的设备型号；
- 阴影图层与原图层之间的关联。

发布干净版本时，不要包含只属于你个人电脑的设置。

## 故障排查

### 背景已应用，但预览显示的是其他媒体

检查该模板在 L-Connect 配置文件中是否有自定义背景。辅助模块会按照选择的设备型号过滤自定义背景路径，因为某些模板 ID 同时存在于 LCD-S 和 LCD-C 系列中。

### 访问被拒绝

请以管理员身份运行 PowerShell 或 `.exe`。L-Connect 将模板保存在 `C:\ProgramData` 下，写入该位置可能需要提升权限。

### 启动时语言、主题或设备不正确

请使用最新 Release。`0.985` 版本修复了启动时程序自动选择控件可能覆盖已保存设置的问题。

## 开发说明

- `editor.ps1` 包含 WPF 界面和主要用户流程。
- `supporter.ps1` 执行 L-Connect 模板和配置文件的底层操作。
- 如果旁边存在 `supporter.exe`，编辑器会优先使用它；否则使用 `supporter.ps1`。
- 设备相关资源会同时从 `ProgramData` 和 L-Connect `Assets` 中查找。
- LCD-C 的默认模板使用相同的对象模型，但预览应按圆形遮罩处理。
- L-Connect 中的部分自定义主题/配置数据与普通 `GraphList` 图层分开保存。

## 免责声明

请自行承担使用风险。编辑 L-Connect 模板前务必保留备份。风扇和水泵控制由 L-Connect 服务处理，因此测试 LCD 主题时请避免不必要的服务重启。
