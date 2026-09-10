# zcode-plugins

ks-c 的个人 ZCode 插件合集。每个插件在 `plugins/` 目录下自包含（含 `plugin.json`
清单与组件），本仓库根目录的 `marketplace.json` 是市场清单。

## 包含的插件

| 插件 | 说明 |
|------|------|
| [always-notify-sound](plugins/always-notify-sound/) | ZCode 需要确认时始终有提示音：窗口聚焦时插件发声，离开窗口时内置通知发声；音量可在插件管理界面配置（0-100） |

## 安装方法

1. 打开 ZCode：**Settings → 插件管理 → 发现 → "+" → GitHub 仓库**
2. 填入：`https://github.com/ks-c/zcode-plugins`
3. 在发现列表找到目标插件 → **获取 / 安装**
4. 安装后默认启用，即可在插件管理界面看到并配置

> 本地手动安装（不经过市场）的替代方式：在 `~/.zcode/cli/config.json` 的
> `plugins.dirs` 中加入插件目录路径，然后重启 ZCode。

## 添加新插件

1. 新建 `plugins/<插件名>/` 目录（内含 `.zcode-plugin/plugin.json` 等，保持自包含）
2. 在 `marketplace.json` 的 `plugins` 数组中追加一条记录（格式与现有条目一致）
3. 提交推送后，已添加本市场的用户刷新/重启客户端即可看到新插件

## 许可

各插件目录内自带许可声明；仓库默认 MIT。
