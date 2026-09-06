# 如何使用

## 前置操作

- 替换所有的`XFEstudio`为您的`主体`，其可以是您个人也可以是公司名称
- 替换所有的`XFE工具箱`为软件的`显示名称`
- 替换所有的`XFEToolBox`为软件的`exe可执行文件名称`
- 将安装包图标放置在`Resources\Icon`目录下，命名为`Icon.ico`
- 将软件本体图标放置在`Resources\Icon`目录下，命名为`Icon.png`

## 将软件压缩包放入安装器

- 发布 XFEToolBox 客户端，将客户端的程序和依赖文件压缩为 `Source.zip`，放入 `Resources\Resource` 目录。压缩包根目录应直接包含 `XFEToolBox.exe`，不要增加二级目录。
- `Source.zip` 不需要包含 `Installer.exe`。安装器会自动将当前运行的单文件安装器复制到安装目录，统一命名为 `Installer.exe`，供客户端后续在线升级使用。下载后的安装器即使被重命名，也会使用这个固定名称。
- 安装器副本和软件文件共用备份、替换及回滚流程；复制失败不会被当作安装成功。兼容旧安装包：如果包内已经显式提供了 `Installer.exe`，则优先使用包内版本。

## 在线升级协议

- 客户端通过 `XFEExtension.NetCore.UpgradeHelper` 请求 `http://upgrade.api.xfe.studio/upgrade`，升级服务中的应用名固定为 `XFEToolBox`
- 检测到新版本并经用户确认后，客户端以管理员权限调用同目录的 `Installer.exe`
- 调用参数依次为：`Upgrade`、升级压缩包的 HTTP/HTTPS 地址、当前 XFEToolBox 安装目录
- 等价命令：`Installer.exe Upgrade <downloadUrl> <installDirectory>`
- Installer 会将压缩包下载为安装目录下的 `InstallPackage.zip`，解压覆盖完成后删除临时压缩包，并启动 `XFEToolBox.exe`
- 发布升级包时不要增加二级目录；压缩包根目录应直接包含 `XFEToolBox.exe` 及其依赖文件，也无需包含 `Installer.exe`
- 在线升级包不要覆盖正在运行的 `Installer.exe`；Installer 自身需要更新时，应在后续安装包发布流程中单独替换

## 推荐发布流程

1. 发布 XFEToolBox 客户端，仅将软件文件压缩为 `Source.zip`，打包时排除旧的 `Installer.exe` 和临时 `InstallPackage.zip`。
2. 将 `Source.zip` 放入 `XFEToolBox.Client.Installer/Resources/Resource`，然后发布安装器：

   ```powershell
   dotnet publish XFEToolBox.Client.Installer/XFEToolBox.Client.Installer.csproj -c Release -p:PublishProfile=FolderProfile
   ```

3. 分发生成的单文件 `Installer.exe`。安装后，软件目录会自动得到一份同样的安装器，保留后续在线升级能力，无需预先再打包一份升级器。

`FolderProfile` 使用 Windows x64、自包含、单文件发布，以保证复制的 `Installer.exe` 可以独立运行。自动复制会保留安装器本身的内置安装包，但 `Source.zip` 不再嵌套第二份安装器。

可用 `-p:InstallationPackagePath=<Source.zip 的绝对路径>` 指定其他安装包，资源名称保持不变。只需要单独分发不含离线安装包的升级器时，仍可使用 `-p:EmbedInstallationPackage=false`。

安装时若目标目录中的 `XFEToolBox.exe` 仍在运行，Installer 会先尝试正常关闭，超时后仅终止路径完全匹配的目标进程；不会按进程名结束其他目录中的同名程序。升级包中若包含正在运行的 Installer 本身，该文件会被跳过，其余应用文件继续安装。

在线升级会等待下载任务结束并关闭所有安装包文件流后，再进入安装页面；下载进度达到 100% 本身不会触发安装。暂停后恢复也会等待上一轮下载释放文件，避免多个下载任务同时写入安装包。

安装包读取、解压、文件属性修改、备份、替换及回滚遇到 Windows 临时共享冲突、锁冲突或拒绝访问时，会自动等待并重试，每个操作最多等待 10 秒。持续占用仍会报告具体失败文件并尝试回滚；无效压缩包、磁盘空间不足等错误不会按文件占用重复尝试。升级成功后，临时安装包清理失败不会把已经完成的升级误报为安装失败。

安装器回归测试（包含自动添加升级器、同目录升级、安装回滚、真实文件锁以及下载完成、暂停恢复和页面卸载的时序测试）：

```powershell
dotnet run --project XFEToolBox.Client.Wpf.Test/XFEToolBox.Client.Wpf.Test.csproj -c Release -p:EmbedInstallationPackage=false -- --tests --filter Installer --no-parallel --report none
```
