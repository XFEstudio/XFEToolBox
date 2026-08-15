# 软件下载目录

客户端的“下载专区”通过工具服务器的 `v1/software/list` 接口读取软件信息，不再从客户端资源文件中读取固定下载地址。

服务器首次启动时会通过 AutoConfig 在 `MainDataProfile` 中创建默认目录。管理员可以维护 `SoftwareCatalog` 列表；修改配置后重启服务器即可生效。

## 字段

| 字段 | 说明 |
| --- | --- |
| `Id` | 软件的稳定唯一标识，用于客户端图标回退等场景 |
| `Name` / `Summary` / `Description` | 名称、卡片摘要与详情介绍 |
| `Publisher` / `Category` / `Version` | 发布者、分类与显示版本 |
| `DownloadUrl` | 由服务器下发的 HTTP(S) 获取地址 |
| `WebsiteUrl` | 可选的官方网站 |
| `IconUrl` | 可选的 HTTP(S) 或 `data:image` 图标；失败时使用客户端默认图标 |
| `FileName` | 直接下载模式的可选保存文件名 |
| `Sha256` | 可选的文件 SHA-256，配置后客户端会强制校验 |
| `Notice` | 需要在详情页特别展示的提示 |
| `Tags` | 服务端搜索标签 |
| `DownloadMode` | `Direct` 表示客户端下载，`Browser` 表示打开官方页面 |
| `Featured` / `Enabled` | 是否优先展示、是否对客户端可见 |

## 接口

`POST /api/v1/software/list`，请求体：

```json
{
  "search": "开发",
  "category": "开发工具"
}
```

`search` 与 `category` 可以为空。接口只返回已启用且下载地址合法的软件，并同时返回完整分类列表。

直接下载会保存到客户端 `DownloadProfile.DownloadDirectory`。若启用了下载完成后自动运行或打开目录，客户端会继续遵守这些设置；同名文件不会覆盖，而是自动添加序号。
