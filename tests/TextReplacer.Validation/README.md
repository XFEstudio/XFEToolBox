# 文本替换工具验证

```powershell
dotnet run --project tests/TextReplacer.Validation/TextReplacer.Validation.csproj
```

链接 EditorWorkspaces/BulkTextReplacer 的实际源代码；覆盖普通文本替换、页签顺序与状态隔离、最小窗口、绑定检查、原有文件替换、编码和备份及宿主编译。文件测试只写入输出目录内新建的专用测试目录，不处理用户文件，也不修改用户剪贴板。

截图和测试报告保存在输出目录 `test-artifacts`。添加 `-- --check-package --register` 可以校验当前 manifest 版本对应的工具包、编译解包源码，并刷新工具工坊项目名称。
