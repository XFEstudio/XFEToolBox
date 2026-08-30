# 聊天模块

本文档说明当前聊天模块的功能、部署方式、数据存储、接口和已知边界。文档以仓库中的实际实现为准。

## 功能范围

聊天入口只对已登录用户开放。客户端调用聊天 HTTP 接口时会携带现有登录会话，服务端聊天服务继承用户认证服务基类，并在仓储层再次校验好友、会话和群成员权限。用户退出时，客户端会先尽力调用服务端 `logout` 撤销当前会话；即使网络请求失败，也会在 `finally` 中清除本地会话，无法在线撤销的服务端令牌随后按原有会话有效期失效。

当前实现包括：

- 好友：搜索用户、发送和处理好友申请、查看及删除好友。
- 群聊：创建公开或私密群聊、公开群大厅推荐、按 12 位精确群号查找和加入私密群、邀请好友、处理群邀请、修改群资料、成员列表、管理员设置、移除成员、转让群主和退群。
- 群邀请：邀请只允许发给好友；邀请记录有效期为 7 天，同时会在双方私聊中生成一条 `GroupInvitation` 邀请卡片消息。
- 消息：好友私聊和群聊中的文本、图片、视频文件及普通文件；支持回复某条消息。服务端按会话分配递增序号，并用发送者与 `clientMessageId` 做幂等保护。
- 附件：分块上传、SHA-256 完整性校验、分块下载和会话权限校验。客户端当前使用 192 KiB 分块，服务端上限由配置控制。
- 实时通知：好友、群组和新消息事件通过认证 WebSocket 推送。消息先持久化，再尽力推送；客户端重连并收到 `realtime.connected` 后，会重新读取会话、好友申请、群组、群邀请、公开群推荐，并重新加载当前选中会话的消息历史。
- 语音通话：好友一对一通话和群聊多人通话；每位远端成员可独立静音或调节到 `0%`～`150%`，本地麦克风可静音或调节到 `0%`～`300%`。

消息类型为 `Text(0)`、`Image(1)`、`Video(2)`、`File(3)`、`System(10)` 和 `GroupInvitation(11)`。客户端只能直接发送前四种；系统和邀请卡片由服务端生成。

群成员角色为 `Member(10)`、`Administrator(50)` 和 `Owner(100)`。公开群会进入大厅推荐；私密群不会出现在推荐列表中，但已登录用户可使用精确群号查找并加入。群号由加密安全随机数生成器生成，为首位非零的 12 位数字；`lookup` 与 `join` 共用每用户每分钟 30 次的内存限流窗口，无效群号也会计入次数。

## 架构

模块由以下部分组成：

1. WPF 客户端聊天页负责好友、群聊、会话、消息和附件交互。
2. `ChatApiClient` 通过现有 ServerInteractive HTTP 通道调用业务接口；共享 DTO 位于 `XFEToolBox.Core`。
3. 服务端聊天服务执行登录态、参数和权限校验，`ChatRepository` 使用 SQLite 持久化好友、群组、会话、消息和附件元数据。
4. `ChatRealtimeBroker` 使用短期一次性票据建立 WebSocket，推送持久化后的领域事件并转发 WebRTC 信令。
5. 通话窗口在 WebView2 中运行 WebRTC/Web Audio。音频媒体不经过聊天 WebSocket 或聊天服务器；小规模通话由参与者之间的 WebRTC Mesh 承载。

SQLite 使用 WAL 模式、外键和忙等待超时。聊天数据库只保存用户 ID，不复制现有用户账号资料，因此恢复数据时还必须保留与之匹配的用户资料数据。

## 数据与附件路径

默认配置为：

- SQLite：`Data/Chat/chat.db`
- 附件根目录：`Data/Chat/Attachments`

相对路径以服务端进程的 `AppContext.BaseDirectory` 为基准解析；也可以配置绝对路径。WAL 运行期间数据库旁可能存在 `chat.db-wal` 和 `chat.db-shm`。

附件使用随机附件 ID 生成存储键，并按 ID 前两位分目录。上传中的文件扩展名为 `.upload`，完成 SHA-256 校验后改为 `.bin`；原始文件名和 Content-Type 保存在 SQLite 中。服务端会清理文件名并验证解析后的路径仍位于附件根目录内。

## HTTP 接口

聊天服务沿用项目现有的 ServerInteractive 协议。聊天业务入口和实时票据入口只接受 POST。基础地址默认为 `<服务器地址>/api`，请求 URL 还要追加下表中的入口点，例如好友列表为 `POST <服务器地址>/api/v1/chat/friends/list`。JSON 请求体中的 `execute` 使用相同入口点，并携带 `session` 与 `deviceInfo`：

```json
{
  "execute": "v1/chat/friends/list",
  "session": "<登录会话>",
  "deviceInfo": "<设备信息>"
}
```

下表列出全部 29 个聊天 HTTP 入口点。除表中特别列出的业务字段外，所有入口都要求有效登录会话。

### 用户与好友

| `execute` | 业务字段 | 作用 |
| --- | --- | --- |
| `v1/chat/users/search` | `query`, `limit` | 按用户名或昵称搜索可用用户 |
| `v1/chat/friends/list` | 无 | 获取好友及对应私聊会话 |
| `v1/chat/friends/requests` | 无 | 获取发出和收到的好友申请 |
| `v1/chat/friends/request` | `targetUserId`, `message` | 发送好友申请 |
| `v1/chat/friends/respond` | `requestId`, `accept` | 接受或拒绝收到的申请 |
| `v1/chat/friends/delete` | `friendUserId` | 删除好友关系 |

### 群聊

| `execute` | 业务字段 | 作用 |
| --- | --- | --- |
| `v1/chat/groups/recommended` | `query`, `limit`, `offset` | 分页搜索大厅公开群推荐 |
| `v1/chat/groups/mine` | 无 | 获取当前用户加入的群聊 |
| `v1/chat/groups/lookup` | `groupNumber` | 按精确群号查找群聊，包括私密群 |
| `v1/chat/groups/create` | `name`, `description`, `isPublic` | 创建群聊，创建者成为群主 |
| `v1/chat/groups/join` | `groupNumber` | 按精确群号加入群聊 |
| `v1/chat/groups/invite` | `groupId`, `friendUserId`, `message` | 邀请好友并生成邀请卡片 |
| `v1/chat/groups/invitations` | 无 | 获取当前用户相关的群邀请 |
| `v1/chat/groups/invitations/respond` | `invitationId`, `accept` | 接受或拒绝群邀请 |
| `v1/chat/groups/update` | `groupId`, `name`, `description`, `isPublic` | 管理员或群主修改群资料和可见性 |
| `v1/chat/groups/members` | `groupId` | 获取群成员列表 |
| `v1/chat/groups/members/remove` | `groupId`, `userId` | 按角色权限移除成员 |
| `v1/chat/groups/members/role` | `groupId`, `userId`, `role` | 群主设置成员或管理员角色 |
| `v1/chat/groups/owner/transfer` | `groupId`, `userId` | 群主转让群所有权 |
| `v1/chat/groups/leave` | `groupId` | 退出群聊；群主需先转让所有权 |

`v1/chat/groups/lookup` 和 `v1/chat/groups/join` 只接受 12 位数字群号，并共享每用户 30 次/分钟的尝试额度；超过额度返回 HTTP `429 Too Many Requests`。该限流状态保存在当前服务端进程内，服务重启后重新计数。

### 会话与消息

| `execute` | 业务字段 | 作用 |
| --- | --- | --- |
| `v1/chat/conversations/list` | 无 | 获取当前用户可访问的会话及最后一条消息 |
| `v1/chat/conversations/direct` | `friendUserId` | 获取或创建与好友的私聊会话 |
| `v1/chat/messages/history` | `conversationId`, `beforeSequence`, `limit` | 按会话序号向前分页读取历史消息 |
| `v1/chat/messages/send` | `conversationId`, `messageType`, `text`, `attachmentId`, `clientMessageId`, `replyToMessageId` | 发送文本、图片、视频文件或普通文件消息 |

### 附件

| `execute` | 业务字段 | 作用 |
| --- | --- | --- |
| `v1/chat/attachments/init` | `fileName`, `contentType`, `totalBytes`, `sha256` | 创建上传记录和临时文件 |
| `v1/chat/attachments/chunk` | `attachmentId`, `offset`, `chunkBase64` | 按连续偏移量上传 Base64 分块 |
| `v1/chat/attachments/complete` | `attachmentId`, `sha256` | 完成上传并验证整个文件的 SHA-256 |
| `v1/chat/attachments/download-chunk` | `attachmentId`, `offset`, `length` | 在附件所有者或会话成员权限下分块下载 |

图片消息要求附件 Content-Type 为 `image/*`，视频文件消息要求为 `video/*`。附件必须由发送者上传、完成校验且尚未用于另一条消息。

### 实时票据

| `execute` | 业务字段 | 作用 |
| --- | --- | --- |
| `v1/chat/realtime/ticket` | 可选 `audience`, `channel` | 用已认证 HTTP 会话换取短期、一次性 WebSocket 票据 |

固定用途值为：

- `audience`: `chat.realtime`
- `channel`: `chat`

响应包含 `ticket`、`expiresAtUtc`、`audience` 和 `channel`。长期登录会话不会放入 WebSocket URL；票据在服务端同时绑定签发它的登录会话 ID。当前最多同时保留 10,000 张尚未消费的票据，每用户最多 5 张；过期限额会在签发新票据时清理，达到限额时接口返回 HTTP `429 Too Many Requests`。

## WebSocket 连接与协议

连接地址为：

```text
wss://<主机>/api/chat/realtime?ticket=<一次性票据>&audience=chat.realtime&channel=chat
```

在仅限本机的开发环境中可使用 `ws://`；跨网络部署应使用 `wss://`。票据默认有效 45 秒，服务端会把配置值限制在 30～60 秒。票据只能消费一次，并同时绑定 audience、channel 和来源登录会话；服务端内存中只保存票据的 SHA-256 摘要。WebSocket 在处理客户端事件及周期维护时会复核该会话，退出、改密、禁用账号或会话过期后会关闭连接。因为票据仍会短暂出现在 URL 查询串中，反向代理和访问日志应隐藏或不记录该查询串。

每个文本消息使用协议版本 1 的 JSON 信封：

```json
{
  "version": 1,
  "eventId": "客户端或服务端生成的唯一 ID",
  "type": "事件类型",
  "occurredAtUtc": "2026-01-01T00:00:00Z",
  "conversationId": "可选会话 ID",
  "actorUserId": "可选操作者 ID",
  "payload": {}
}
```

当前服务端最多接受 10,000 个实时连接，每用户最多 5 个。服务端只接受完整、未分片且不超过 64 KiB 的文本帧，拒绝二进制帧；单连接限 10 秒 160 帧。服务器公布的心跳间隔为 20 秒，75 秒未活动的连接会被回收。这些票据与连接容量是当前代码常量，不是 `ServerProfile` 配置项。

### 客户端发往服务端

- `ping`、`pong`：连接保活；服务端用 `pong` 回应 `ping`。
- `event.ack`：允许客户端确认事件，当前服务端不据此建立离线投递队列。
- `call.invite`：`payload` 包含 `callId`、`targetType`（`user` 或 `group`）、`targetId`；好友呼叫会验证好友关系，群呼叫会验证群成员身份。每个用户最多同时作为创建者保留 3 个活跃通话房间；群呼叫取群成员作为候选接收者，当前列表最多 64 人（该计数包含发起者，实际推送时再排除发起者）。
- `call.accept`、`call.join`、`call.reject`、`call.leave`：`payload` 包含 `callId`。
- `webrtc.offer`、`webrtc.answer`、`webrtc.ice`：`payload` 包含 `callId`、`targetUserId` 以及 SDP 或 ICE 信令内容；服务端只向同一通话内的目标参与者转发。

服务端会对支持的操作返回 `event.ack`；无效参数、权限、容量或协议错误返回 `event.error`，其中包含 `code`、`message` 和可选 `replyTo`。

### 服务端发往客户端

连接及通话事件：

- `realtime.connected`
- `pong`
- `event.ack`
- `event.error`
- `call.invite`
- `call.created`
- `call.accept`
- `call.join`
- `call.reject`
- `call.leave`
- `call.ended`
- `webrtc.offer`
- `webrtc.answer`
- `webrtc.ice`

持久化业务事件：

- `chat.message.created`
- `chat.friend.requested`
- `chat.friend.request.updated`
- `chat.friend.deleted`
- `chat.group.invited`
- `chat.group.invitation.updated`
- `chat.group.updated`
- `chat.group.members.updated`

WebSocket 事件用于让在线界面及时刷新，不替代持久化接口，也不保证断线期间的事件补发。当前客户端在每次连接或重连收到 `realtime.connected` 后主动重新读取持久化数据，因此恢复依赖 HTTP 查询，而不是实时事件重放。

## WebRTC、降噪与低延迟边界

语音通话使用 WebRTC 音频和优先的 Opus 编解码器。客户端请求浏览器/WebView2 提供：

- `echoCancellation`：回声消除（AEC）
- `noiseSuppression`：平台噪声抑制（NS）
- `autoGainControl`：自动增益控制（AGC）
- 在运行时支持时启用 `voiceIsolation`
- Web Audio 80 Hz 高通、12 kHz 低通、动态压缩/限幅和独立 GainNode

这里的“降噪”是 WebRTC/浏览器提供的音频处理能力加本地滤波链，`voiceIsolation` 也只在运行环境支持时生效；当前仓库**没有集成独立的神经网络 AI 降噪模型**，因此不应把它描述为自研或专用 AI 降噪。若产品必须具备可验证的神经网络降噪，需要另行选型、集成模型并测量 CPU/GPU 占用和端到端延迟。

当前通话是最多 8 人的小规模 Mesh。每位参与者都要与其他参与者建立连接，人数增加时上行带宽、CPU 和连接数近似随 Mesh 规模快速增长。8 人是当前服务端硬限制和客户端默认上限，不代表所有设备及网络都能稳定承载 8 人。服务端还限制每个用户最多创建 3 个活跃通话房间，并拒绝群成员候选列表超过 64 人的群语音邀请；WPF 客户端界面本身一次只允许一个本地活跃通话。超过 8 人、需要稳定的大房间、服务端录制或统一带宽控制时，应引入 SFU；现有服务端不包含 SFU。

P2P、Opus、较短的本地处理链和 WebSocket 信令以降低延迟为目标，但不能保证固定或绝对的低延迟。真实延迟取决于双方网络、NAT 类型、是否经过 TURN、中继位置、丢包、抖动、设备驱动和系统负载。界面会从 WebRTC 统计中显示 RTT、抖动和丢包，部署后应在目标网络中压测。

## HTTPS/WSS 与 TURN

客户端默认 API 地址现为 `https://toolbox.api.xfe.studio/api`。可通过客户端进程环境变量 `XFETOOLBOX_API_ADDRESS` 指定完整 API 根地址；客户端只采用绝对 `https://` 地址，或仅限 loopback 主机的 `http://` 地址。非 loopback 的明文 HTTP 配置不会被采用，而是回退到默认 HTTPS 地址。

服务端启动代码仍实际绑定 `ServerProfile.HttpAddress`；`HttpsAddress` 虽已定义，但尚未接入当前绑定流程。因此生产部署仍需要在服务端前放置支持 WebSocket Upgrade 的 TLS 反向代理：

- 将 HTTPS 的 `/api` 转发到内部 HTTP `/api`。
- 将 WSS 的 `/api/chat/realtime` 转发到内部 WebSocket，并保留查询参数和 Upgrade 头。
- 客户端远程 API 地址必须使用 `https://`，实时客户端会据此派生 `wss://`；只有 loopback 开发地址允许 `http://`/`ws://`。
- `XFETOOLBOX_API_ADDRESS` 应包含完整 `/api` 路径，例如 `https://toolbox.example.com/api`。
- 为票据查询参数关闭或脱敏访问日志。

默认 ICE 列表只有公共 STUN。对称 NAT、严格企业防火墙或运营商网络下，只有 STUN 可能无法建立媒体连接，生产环境需要部署尽量靠近用户的 TURN，并同时提供 UDP 和受控的 TCP/TLS 回退。

客户端进程通过以下环境变量读取 ICE/TURN 配置：

| 环境变量 | 说明 |
| --- | --- |
| `XFETOOLBOX_WEBRTC_ICE_SERVERS` | 用分号分隔的 STUN/TURN URL，例如 `stun:stun.example.com:3478;turn:turn.example.com:3478?transport=udp;turns:turn.example.com:5349?transport=tcp` |
| `XFETOOLBOX_TURN_USERNAME` | TURN 用户名 |
| `XFETOOLBOX_TURN_CREDENTIAL` | TURN 凭据 |

不要把长期 TURN 凭据提交到仓库。应通过操作系统密钥管理或部署系统注入，并按 TURN 服务策略轮换。当前环境变量模型对整组 URL 使用同一组用户名和凭据。

## 服务端配置

首次运行由 AutoConfig 生成 `ServerProfile` XML。与聊天直接相关的配置如下：

| 配置项 | 默认值 | 当前作用 |
| --- | --- | --- |
| `ChatDatabasePath` | `Data/Chat/chat.db` | SQLite 数据库路径 |
| `ChatAttachmentStorageRoot` | `Data/Chat/Attachments` | 附件文件根目录 |
| `MaxChatAttachmentBytes` | `536870912`（512 MiB） | 单附件最大字节数 |
| `MaxChatAttachmentChunkBytes` | `4194304`（4 MiB） | 单次上传或下载分块最大字节数；仓储还限制为不超过 16 MiB 且不超过单附件上限 |
| `RealtimeTicketLifetimeSeconds` | `45` | 实时票据有效期，启动时限制到 30～60 秒 |
| `HttpAddress` | `http://localhost:3000/` | 当前服务端实际监听地址 |
| `HttpsAddress` | `https://localhost:3400/` | 已定义但当前启动代码未绑定；参见反向代理部署说明 |

路径和限制应在停服后修改并重启。调整上限前还应检查反向代理请求体限制、磁盘配额、Base64 约 4/3 的传输膨胀以及服务端内存压力。

## 备份与恢复

聊天恢复需要把数据库、附件和现有用户资料视为同一份数据集。推荐流程：

1. 停止服务端，避免备份期间 SQLite 和附件继续变化。
2. 复制 `ChatDatabasePath` 指向的数据库；停服后确认 WAL 已正常收束。若必须在线备份，应使用 SQLite Online Backup API 或先执行受控 checkpoint，不能只在写入期间随意复制 `chat.db` 而忽略 `-wal`。
3. 在同一个一致性时间点完整复制 `ChatAttachmentStorageRoot`。
4. 同时备份 AutoConfig 管理的用户资料和服务端配置。聊天数据库引用现有用户 ID，仅恢复聊天库无法重建账号资料。
5. 恢复时保持数据库与附件成对，校验目录权限、可用空间和配置路径，再启动服务端并检查日志中的“聊天”和“附件”绝对路径。

不要只备份 SQLite 或只备份附件目录：前者会留下缺失文件的附件记录，后者则无法恢复消息与访问权限关系。

## 当前未实现或需后续工程化的能力

- 未读状态尚未持久化：没有每用户 `last-read` 游标，`ChatConversationInfo.UnreadCount` 当前返回 `0`。
- 尚未实现消息送达回执和已读回执；`event.ack` 只确认实时协议操作，不是消息送达或已读语义。
- 尚未实现附件孤儿的定时清理，包括放弃、超时或进程中断后遗留的 `Uploading` 记录与 `.upload` 临时文件，以及已经完成但最终没有关联到消息的附件记录与 `.bin` 文件。生产部署前应增加带保留期、数据库引用校验、并发保护和指标记录的清理任务。
- WebSocket 领域事件没有离线队列；当前客户端重连后会自动重新读取会话、好友/群邀请和消息历史来收敛状态，但这不是实时事件补发。
- 当前多人语音仅适合最多 8 人的小规模 Mesh，不包含 SFU，也不提供超出实际网络条件的低延迟保证。
