# always-notify-sound

ZCode 需要你操作时**始终**播放提示音——**任何窗口、任何对话、无论 ZCode 是否在前台**，
都不会漏报。

覆盖三类时机：

1. **需要你授权**：工具权限确认弹窗出现时（`PermissionRequest`）
2. **需要你回答**：助手向你提问征求确认时（`AskUserQuestion`）
3. **轮到你操作**：助手完成一轮回复、在等你输入时（`Stop`，可在界面关闭）

## 原理

ZCode 内置的任务通知（`show-task-notification`）在窗口聚焦时会被跳过
（`isAnyAppWindowFocused` 检查），导致"在窗口里就没有提示音"。本插件通过 hook 在
这些时机直接运行 `hooks/chime.exe` 播放系统提示音，**不依赖窗口焦点**：

| hook 事件            | 触发时机                             | 传参 |
|----------------------|--------------------------------------|------|
| `PermissionRequest`  | 工具调用需要你授权（确认弹窗出现时） | `--event permission` |
| `PreToolUse`（`AskUserQuestion`） | 助手向你提问征求确认 | `--event question` |
| `Stop`               | 助手完成一轮回复、等待你输入 | `--event stop` |

声音由 `hooks/chime.exe` 播放 `hooks/chime.wav`（合成双音提示音）；同一确认瞬间
触发的多个 hook 事件会在 500ms 窗口内去重，只响一次；去重按**对话会话隔离**
（依据 hook 注入的会话 ID），所以其他对话的提示音不会吞掉当前对话的这一声。
hook 始终返回 0，不会阻塞或报错会话。

> 说明：因为插件在离窗时也会发声，而 ZCode 内置通知在离窗时同样会发声，所以**离开
> 窗口**时可能听到内置通知音 + 插件音各一次（约相差 0.3 秒）。这是"绝不漏报"的代价；
> 插件音与内置音都可在系统音量里调节。

## 安装

### 方式一：通过市场安装（推荐）

1. **Settings → 插件管理 → 发现 → "+" → GitHub 仓库**
2. 填入：`https://github.com/ks-c/zcode-plugins`
3. 找到 **always-notify-sound** → 获取/安装 → 默认启用

### 方式二：本地目录加载（不经过市场）

在 `~/.zcode/cli/config.json` 的 `plugins.dirs` 中加入插件目录（默认启用）：

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
  - 在本目录 `hooks/` 里创建一个名为 `disabled.flag` 的空文件即可停用（删除即恢复）；
  - 设置环境变量 `ZCODE_NOTIFY_SOUND=0`（或 `off`）。

## 自定义配置

在 **Settings → Plugin Management → always-notify-sound → 详情 → Advanced** 中设置，
修改后即时生效：

| 配置项 | 类型 | 默认 | 说明 |
|--------|------|------|------|
| 提示音音量 | 数字 | 100 | 0-100（0 为静音） |
| 任务结束时也提示 | 开关 | 开启 | 关闭后只在需要授权/回答提问时提示，助手普通回复结束时不响 |

也支持命令行/文件方式设置音量（优先级：界面配置 > 环境变量 > 文件）：

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
| `.zcode-plugin/plugin.json` | 插件清单（含 `userConfig`：音量、任务结束提示开关） |
| `hooks/hooks.json` | hook 声明（PermissionRequest + AskUserQuestion + Stop） |
| `hooks/chime.exe` | 提示音播放程序（音量读取 + 采样缩放，.NET Framework 编译，亦可用 csc 从 `chime.cs` 重新编译） |
| `hooks/chime.cs` | 提示音程序源码 |
| `hooks/chime.wav` | 提示音音频（44.1kHz 16bit 双音合成） |
