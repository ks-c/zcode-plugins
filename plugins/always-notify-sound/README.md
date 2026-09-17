# always-notify-sound

ZCode 需要你确认时（工具权限确认、AskUserQuestion 提问）**始终**有提示音：

1. **离开窗口时**：走 ZCode 内置通知（系统弹窗 + 内置提示音，原有行为不变）
2. **不离开窗口时**：内置通知会被窗口聚焦检查跳过，本插件补上这一声提示音

本插件只在 ZCode 窗口聚焦时发声，离窗时交给内置音效，两个场景不重不漏。

## 原理

ZCode 内置的任务通知（`show-task-notification`）在窗口聚焦时会被跳过
（`isAnyAppWindowFocused` 检查），因此只有离开窗口才有提示音。本插件通过 hook 在
确认时机运行 `hooks/chime.exe`，它会检测前台窗口：**前台是 ZCode → 播放提示音；
前台不是 ZCode → 跳过**（让内置音效负责）。从而补齐"聚焦窗口无提示音"的缺口：

| hook 事件            | 触发时机                             |
|----------------------|--------------------------------------|
| `PermissionRequest`  | 工具调用需要你授权（确认弹窗出现时） |
| `PreToolUse` / `PostToolUse`（`AskUserQuestion`） | 助手向你提问征求确认 |

声音由 `hooks/chime.exe` 播放 `hooks/chime.wav`（合成双音提示音）；同一确认瞬间
触发的多个 hook 事件会在 500ms 窗口内去重，只响一次；去重按**对话会话隔离**
（依据 hook 注入的会话 ID），所以其他对话的提示音不会吞掉当前对话的这一声。
hook 始终返回 0，不会阻塞或报错会话。

## 安装

该插件通过用户配置 `~/.zcode/cli/config.json` 的 `plugins.dirs` 加载（默认启用，
无需 marketplace）：

```jsonc
{
  "plugins": {
    "dirs": ["C:\\Users\\Administrator\\.zcode\\plugins\\always-notify-sound"]
  }
}
```

修改后**重启 ZCode**（桌面应用在启动时读取插件与 hook 配置，当前运行中的会话不会
热加载）。

## 启用 / 停用

- 在 **Settings → Plugin Management** 中可开关该插件（`always-notify-sound`）。
- 或者以下任一种方式临时停用提示音，无需重启：
  - 删除/重命名本目录 `hooks/` 里的 `disabled.flag` 文件（创建一个名为
    `disabled.flag` 的空文件即可停用）；
  - 设置环境变量 `ZCODE_NOTIFY_SOUND=0`（或 `off`）。

## 自定义音量

优先级从高到低：

1. **插件管理界面**（推荐）：**Settings → Plugin Management → always-notify-sound
   → 详情 → Advanced**，填写"提示音音量"（0-100，默认 100）。修改后即时生效。
2. 环境变量 `ZCODE_NOTIFY_SOUND_VOLUME`（需在启动 ZCode 前设置）。
3. 文件方式：`hooks/volume.txt` 写入数字（随时改，立即生效）。

| 写法 | 音量 |
|---|---|
| `100` 或 `1` | 100%（默认，满音量） |
| `80` 或 `0.8` | 80% |
| `50` / `0.5` | 50% |
| `0` | 静音（等同停用） |

```bash
# 文件方式（随时改，立即生效）
echo 60 > C:\Users\Administrator\.zcode\plugins\always-notify-sound\hooks\volume.txt

# 环境变量方式（对 zcode 进程生效，需在启动 zcode 前设置）
set ZCODE_NOTIFY_SOUND_VOLUME=60
```

音量是对音频采样做精确线性缩放（50% = 实际半音量）。

## 卸载

1. 从 `~/.zcode/cli/config.json` 的 `plugins.dirs` 中移除本目录；
2. 删除整个 `C:\Users\Administrator\.zcode\plugins\always-notify-sound` 目录。

## 文件说明

| 文件 | 说明 |
|------|------|
| `.zcode-plugin/plugin.json` | 插件清单（含 `userConfig.volume` 音量配置项） |
| `hooks/hooks.json` | hook 声明（PermissionRequest + AskUserQuestion） |
| `hooks/chime.exe` | 提示音播放程序（焦点检测 + 音量读取 + 采样缩放，.NET Framework 编译，亦可用 csc 从 `chime.cs` 重新编译） |
| `hooks/chime.cs` | 提示音程序源码 |
| `hooks/chime.wav` | 提示音音频（44.1kHz 16bit 双音合成） |