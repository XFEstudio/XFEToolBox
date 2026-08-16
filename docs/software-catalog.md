# 软件下载目录与多渠道配置

客户端“下载专区”通过服务端目录获取软件信息，不在客户端资源中维护固定下载地址。服务端首次启动会在 `MainDataProfile.SoftwareCatalog` 创建默认目录；管理员也可以直接在桌面客户端的管理中心新增、修改、发布、下架、上传或删除软件。

## 软件字段

| 字段 | 说明 |
| --- | --- |
| `Id` | 稳定唯一标识，允许字母、数字、`.`、`_`、`-`，最长 64 个字符 |
| `Name` / `Summary` / `Description` | 名称、卡片摘要与详情介绍 |
| `Publisher` / `Category` / `Version` | 发布者、分类与显示版本 |
| `WebsiteUrl` | 可选官方网站，必须是 HTTP(S) 地址 |
| `IconUrl` | 可选的 HTTP(S) 或 `data:image` 图标；加载失败时使用客户端默认图标 |
| `Notice` | 在详情页突出显示的提示 |
| `Tags` | 搜索标签 |
| `Featured` | 是否优先展示 |
| `Enabled` | 是否启用该目录项 |
| `Published` | 是否已发布给普通客户端 |
| `Channels` | 多渠道下载配置；存在渠道时优先于旧版单地址字段 |

`DownloadUrl`、`FileName`、`Sha256` 和 `DownloadMode` 是为已有 AutoConfig 数据保留的单渠道兼容字段。新配置应使用 `Channels`。

## 下载渠道

每个软件可以配置多个渠道，例如官网、GitHub、国内镜像或服务器托管文件。

| 字段 | 说明 |
| --- | --- |
| `Id` | 软件内唯一的渠道标识 |
| `Name` | 客户端显示名称 |
| `Mode` | `Browser`、`Direct` 或 `Server` |
| `Url` | `Browser`/`Direct` 使用的 HTTP(S) 地址 |
| `FileName` | 客户端保存时建议使用的文件名 |
| `Sha256` | 可选的文件 SHA-256；配置后客户端强制校验 |
| `StorageKey` | 服务端托管文件的内部相对路径，不会返回给公开客户端 |
| `Enabled` | 是否启用该渠道 |
| `SortOrder` | 渠道排序，数值较小者优先 |

三种模式的行为：

- `Browser`：打开渠道 URL，由官网或浏览器继续处理。
- `Direct`：客户端直接下载 URL，并按下载设置决定是否运行文件或打开目录。
- `Server`：客户端调用服务器下载接口；服务器只返回已发布、已启用且文件真实存在的渠道。

直接下载默认保存到 `DownloadProfile.DownloadDirectory`。同名文件不会被覆盖，而是自动添加序号。

## 公开接口

查询目录：

```http
POST /api/v1/software/list
Content-Type: application/json

{
  "search": "开发",
  "category": "开发工具"
}
```

`search` 和 `category` 可以为空字符串。接口只返回 `Enabled` 与 `Published` 均为 `true` 且至少有一个可用渠道的软件，同时返回当前可见的完整分类列表。搜索范围包含名称、摘要、描述、发布者和标签。

下载服务端托管文件：

```http
POST /api/v1/software/download
Content-Type: application/json

{
  "softwareId": "example-app",
  "channelId": "stable"
}
```

只有 `Server` 模式渠道可以使用该接口。

## 管理接口

以下接口要求有效登录会话且当前用户为管理员：

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `POST` | `/api/v1/manage/software/list` | 查看全部软件，包括未发布项目 |
| `POST` | `/api/v1/manage/software/upsert` | 新增或更新软件及其渠道 |
| `POST` | `/api/v1/manage/software/publication` | 发布或下架软件 |
| `POST` | `/api/v1/manage/software/upload` | 为指定 `Server` 渠道上传文件 |
| `POST` | `/api/v1/manage/software/delete` | 删除软件；可同时删除服务器托管文件 |

服务端文件保存到 `ServerProfile.SoftwareStorageRoot`。单文件默认最大 256 MiB，由 `ServerProfile.MaxSoftwareBytes` 控制。上传成功后服务器会记录文件名、存储位置与 SHA-256，并在更新目录项时保留已有的内部存储信息。

## 运维与安全

- 更新 `MainDataProfile` 的 AutoConfig XML 后需要重启服务端；通过管理界面修改则会立即保存。
- 对 `Direct` 渠道尽量配置可信的 HTTPS 地址和 SHA-256；版本更新后同步更新哈希。
- `Server` 渠道只接受经过文件名规范化和存储根目录约束的文件，不要手动把绝对路径写入 `StorageKey`。
- 不要把服务端 `Data` 目录、包含登录信息的配置或真实管理凭据提交到 Git。
