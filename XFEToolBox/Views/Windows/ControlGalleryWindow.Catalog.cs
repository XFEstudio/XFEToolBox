namespace XFEToolBox.Client.Views.Windows;

public partial class ControlGalleryWindow
{
    private static IReadOnlyList<ControlGalleryItem> BuildCatalog() =>
    [
        Item(
            "button", "Button", "System.Windows.Controls.Button", "基础输入", "B",
            "执行页面中的即时操作。",
            "统一按钮包含主次层级、悬停缩放、按压反馈和禁用状态，适合作为工具页面的主要操作入口。",
            """
            <StackPanel Orientation="Horizontal">
                <Button Content="普通操作" Margin="0,0,8,0"/>
                <Button Content="主要操作"
                        controls:ButtonAssist.IsPrimary="True"/>
            </StackPanel>
            """,
            [P("Content", "object", "按钮显示的文字或任意内容"), P("IsPrimary", "bool", "ButtonAssist 提供的主操作外观"), P("Command", "ICommand", "MVVM 命令绑定")],
            ["一个区域通常只保留一个主要按钮", "工具项目会自动加载统一隐式样式"],
            ["按钮", "command", "primary", "click"]),

        Item(
            "text-editor", "TextEditor", "controls:TextEditor", "基础输入", "T",
            "带提示、焦点描边和可选图标的文本输入框。",
            "TextEditor 是工具页面推荐使用的文本输入控件，兼容 TextBox 的文本、换行、验证和双向绑定能力。",
            """
            <controls:TextEditor Width="320" Height="38"
                                 HintText="输入项目名称"
                                 Text="{Binding ProjectName,
                                     UpdateSourceTrigger=PropertyChanged}"/>
            """,
            [P("HintText", "string", "输入为空时显示的提示"), P("Icon", "Geometry", "输入框左侧可选图标"), P("EditorCornerRadius", "CornerRadius", "输入框圆角")],
            ["数据绑定建议使用 PropertyChanged 更新触发器", "多行输入时设置 AcceptsReturn 和 TextWrapping"],
            ["文本框", "输入", "hint", "textbox"]),

        Item(
            "password-editor", "PasswordEditor", "controls:PasswordEditor", "基础输入", "●",
            "支持显隐切换的统一密码输入框。",
            "PasswordEditor 提供密码遮罩、显示切换、只读状态和变更事件，外观与 TextEditor 完全一致。",
            """
            <controls:PasswordEditor Width="320" Height="38"
                                     HintText="输入访问密钥"
                                     Password="{Binding AccessKey}"/>
            """,
            [P("Password", "string", "当前密码文本"), P("PasswordVisible", "bool", "是否显示明文"), P("PasswordMask", "string", "密码遮罩字符")],
            ["敏感数据不应写入日志", "需要即时校验时订阅 PasswordChanged"],
            ["密码", "secret", "password", "显隐"]),

        Item(
            "auto-suggest", "AutoSuggestBox", "controls:AutoSuggestBox", "基础输入", "⌕",
            "输入时实时筛选并支持键盘选择的建议框。",
            "AutoSuggestBox 适合命令、路径、标签和历史记录搜索；输入达到最短前缀长度后自动打开匹配列表。",
            """
            <controls:AutoSuggestBox Width="320" Height="38"
                                     PlaceholderText="搜索开发语言"
                                     MinimumPrefixLength="1"
                                     ItemsSource="{Binding Languages}"/>
            """,
            [P("ItemsSource", "IEnumerable", "可供检索的候选数据"), P("PlaceholderText", "string", "空输入提示"), P("MinimumPrefixLength", "int", "开始提供建议所需的字符数")],
            ["复杂对象可设置 DisplayMemberPath", "候选项使用当前区域性的忽略大小写匹配"],
            ["自动完成", "搜索", "suggest", "filter"]),

        Item(
            "check-box", "CheckBox", "System.Windows.Controls.CheckBox", "基础输入", "✓",
            "用于一个或多个相互独立的选择。",
            "统一 CheckBox 支持未选、已选和不确定状态，适合权限、选项和批量选择场景。",
            """
            <StackPanel>
                <CheckBox Content="启用自动保存" IsChecked="True"/>
                <CheckBox Content="仅同步当前项目" Margin="0,8,0,0"/>
            </StackPanel>
            """,
            [P("IsChecked", "bool?", "选择状态，支持三态"), P("IsThreeState", "bool", "是否允许不确定状态"), P("Content", "object", "选项说明")],
            ["互斥选项应改用 RadioButton", "标签本身可以点击切换"],
            ["复选框", "checkbox", "选择"]),

        Item(
            "radio-button", "RadioButton", "System.Windows.Controls.RadioButton", "基础输入", "○",
            "从一组互斥选项中选择一个。",
            "统一 RadioButton 使用主题色圆点和清晰的悬停状态，通过 GroupName 将同一容器中的选项分组。",
            """
            <StackPanel>
                <RadioButton Content="稳定通道" GroupName="Channel" IsChecked="True"/>
                <RadioButton Content="预览通道" GroupName="Channel" Margin="0,8,0,0"/>
            </StackPanel>
            """,
            [P("IsChecked", "bool?", "当前是否选中"), P("GroupName", "string", "互斥选择的分组名称"), P("Content", "object", "选项内容")],
            ["同组只会保留一个选中项", "默认值应在界面初始化时明确给出"],
            ["单选", "radio", "互斥"]),

        Item(
            "toggle-button", "ToggleButton", "System.Windows.Controls.Primitives.ToggleButton", "基础输入", "◐",
            "具有保持状态的操作按钮。",
            "ToggleButton 适合工具栏模式、固定开关和视图状态；选中后使用主题色高亮，与普通 Button 的即时操作语义不同。",
            """
            <ToggleButton Content="实时预览"
                          IsChecked="{Binding IsLivePreview}"/>
            """,
            [P("IsChecked", "bool?", "保持的切换状态"), P("IsThreeState", "bool", "是否启用第三状态"), P("Command", "ICommand", "状态切换时执行的命令")],
            ["系统设置型开关也可使用 SwitchButton", "不要用 ToggleButton 表达一次性提交操作"],
            ["切换按钮", "toggle", "状态"]),

        Item(
            "combo-box", "ComboBox", "System.Windows.Controls.ComboBox", "基础输入", "⌄",
            "在紧凑空间内提供一组选项。",
            "统一 ComboBox 包含圆角弹层、焦点描边和键盘导航，适合数量有限且不需要搜索的单选数据。",
            """
            <ComboBox Width="260" SelectedIndex="0">
                <ComboBoxItem Content="Debug"/>
                <ComboBoxItem Content="Release"/>
            </ComboBox>
            """,
            [P("ItemsSource", "IEnumerable", "选项集合"), P("SelectedItem", "object", "当前选项"), P("DisplayMemberPath", "string", "复杂对象的显示属性")],
            ["候选项较多时优先使用 AutoSuggestBox", "通过 SelectedValuePath 绑定业务主键"],
            ["下拉框", "combobox", "选择"]),

        Item(
            "slider", "Slider", "System.Windows.Controls.Slider", "选择器", "—",
            "在连续或离散范围中调整数值。",
            "统一 Slider 提供主题色轨道、悬停放大的滑块和键盘步进，适合缩放、音量、透明度和阈值设置。",
            """
            <Slider Width="320" Minimum="0" Maximum="100"
                    Value="65" TickFrequency="5"
                    IsSnapToTickEnabled="True"/>
            """,
            [P("Value", "double", "当前值"), P("Minimum", "double", "允许范围的最小值"), P("Maximum", "double", "允许范围的最大值"), P("TickFrequency", "double", "刻度步长")],
            ["旁边应显示当前精确值", "离散数据可启用 IsSnapToTickEnabled"],
            ["滑块", "slider", "range", "数值"]),

        Item(
            "color-picker", "ColorPicker", "controls:ColorPicker", "选择器", "◆",
            "通过色板、RGB 通道或十六进制选择颜色。",
            "ColorPicker 在紧凑按钮中展示当前色，展开后可选常用主题色并精确输入 RGB/HEX，所有派生值自动同步。",
            """
            <controls:ColorPicker SelectedColor="#9898E7"
                                  SelectedColorChanged="ColorPicker_Changed"/>
            """,
            [P("SelectedColor", "Color", "当前颜色，默认双向绑定"), P("SelectedBrush", "Brush", "只读的当前颜色画刷"), P("HexValue", "string", "可双向绑定的 HEX 文本")],
            ["透明色会保留 Alpha 通道", "输入支持 #RRGGBB 和 #AARRGGBB"],
            ["颜色", "color", "rgb", "hex", "调色板"]),

        Item(
            "date-picker", "DatePicker", "System.Windows.Controls.DatePicker", "选择器", "日",
            "使用系统日期模型选择单个日期。",
            "原生 DatePicker 已接入统一圆角、日历弹层和主题状态，适合不需要自定义文本格式的普通日期选择。",
            """
            <DatePicker Width="240"
                        SelectedDate="{Binding StartDate}"/>
            """,
            [P("SelectedDate", "DateTime?", "当前选择的日期"), P("DisplayDateStart", "DateTime?", "允许显示的起始日期"), P("DisplayDateEnd", "DateTime?", "允许显示的结束日期")],
            ["限制业务日期时同时设置起止范围", "需要固定格式时使用 CalendarPicker"],
            ["日期", "datepicker", "calendar"]),

        Item(
            "calendar-picker", "CalendarPicker", "controls:CalendarPicker", "选择器", "▦",
            "支持自定义显示格式的统一日历选择器。",
            "CalendarPicker 在 DatePicker 基础上提供 DisplayFormat 与 PlaceholderText，更适合工具项目中的版本日期、计划日期等字段。",
            """
            <controls:CalendarPicker Width="240"
                                     DisplayFormat="yyyy年MM月dd日"
                                     PlaceholderText="选择计划日期"
                                     SelectedDate="{Binding PlanDate}"/>
            """,
            [P("DisplayFormat", "string", "按当前区域性格式化日期"), P("PlaceholderText", "string", "空值提示"), P("SelectedDate", "DateTime?", "当前日期，支持双向绑定")],
            ["格式字符串遵循 .NET DateTime 规则", "业务层仍应校验日期范围"],
            ["日历选择", "calendarpicker", "格式化"]),

        Item(
            "time-picker", "TimePicker", "controls:TimePicker", "选择器", "◷",
            "可配置小时、分钟和秒钟的 24 小时时间选择器。",
            "TimePicker 可分别控制小时、分钟和秒钟列，并为分钟与秒钟配置步长；SelectedTime 可直接双向绑定到 TimeSpan?。",
            """
            <controls:TimePicker Width="260"
                                 ShowHour="True"
                                 ShowMinute="True"
                                 ShowSecond="True"
                                 MinuteIncrement="5"
                                 SecondIncrement="1"
                                 PlaceholderText="选择执行时间"
                                 SelectedTime="{Binding RunTime}"/>
            """,
            [P("SelectedTime", "TimeSpan?", "当前时间，默认双向绑定"), P("ShowHour", "bool", "是否显示小时列"), P("ShowMinute", "bool", "是否显示分钟列"), P("ShowSecond", "bool", "是否显示秒钟列"), P("MinuteIncrement", "int", "分钟步长，范围 1–30"), P("SecondIncrement", "int", "秒钟步长，范围 1–30"), P("IsDropDownOpen", "bool", "弹层是否打开")],
            ["默认显示小时和分钟，秒钟按需启用", "“现在”会按对应步长四舍五入"],
            ["时间", "timepicker", "时分秒", "second"]),

        Item(
            "progress-bar", "ProgressBar", "System.Windows.Controls.ProgressBar", "状态反馈", "▰",
            "展示可确定进度或持续进行中的任务。",
            "统一 ProgressBar 使用圆角轨道和主题渐变，既支持 Value 进度，也支持 IsIndeterminate 的未知时长任务。",
            """
            <ProgressBar Width="360" Height="8"
                         Minimum="0" Maximum="100" Value="68"/>
            """,
            [P("Value", "double", "当前进度"), P("Minimum", "double", "进度范围最小值"), P("Maximum", "double", "进度范围最大值"), P("IsIndeterminate", "bool", "是否显示循环动画")],
            ["可确定任务应优先显示真实进度", "长任务旁应补充状态文字和取消操作"],
            ["进度条", "progress", "loading"]),

        Item(
            "progress-ring", "ProgressRing", "controls:ProgressRing", "状态反馈", "◌",
            "支持确定进度与循环状态的环形进度控件。",
            "ProgressRing 既可通过 Minimum、Maximum 和 Value 显示实际进度，也可切换 IsIndeterminate 表示未知时长任务。",
            """
            <controls:ProgressRing Width="42" Height="42"
                                   Minimum="0" Maximum="100" Value="68"
                                   IsIndeterminate="False"
                                   IsActive="True" RingThickness="3"/>
            """,
            [P("Value", "double", "确定模式的当前进度"), P("Minimum", "double", "进度范围最小值"), P("Maximum", "double", "进度范围最大值"), P("IsIndeterminate", "bool", "是否使用循环动画"), P("IsActive", "bool", "是否显示活动弧段"), P("RingThickness", "double", "环形笔画宽度")],
            ["未知进度时启用 IsIndeterminate", "无障碍状态文字应放在控件附近"],
            ["环形进度", "progressring", "busy", "loading"]),

        Item(
            "info-bar", "InfoBar", "controls:InfoBar", "状态反馈", "i",
            "展示页面级提示、成功、警告和错误信息。",
            "InfoBar 提供四种严重程度、可关闭状态和操作区，适合不会阻断当前流程但需要用户注意的消息。",
            """
            <controls:InfoBar Title="构建完成"
                              Message="工具包已生成，可以进行本地测试。"
                              Severity="Success"
                              IsOpen="True"/>
            """,
            [P("Severity", "InfoBarSeverity", "Informational、Success、Warning 或 Error"), P("IsOpen", "bool", "是否显示消息条"), P("ActionContent", "object", "右侧可选操作内容")],
            ["阻断式确认仍应使用 PopupWindow", "错误消息应包含下一步处理建议"],
            ["消息条", "infobar", "提示", "警告", "错误"]),

        Item(
            "command-bar", "CommandBar", "controls:CommandBar", "布局与内容", "⌘",
            "组织页面标题、主要命令和尾部辅助操作。",
            "CommandBar 把页面级操作保持在统一的白色卡片中，Items 区用于主要命令，SecondaryContent 用于筛选或帮助入口。",
            """
            <controls:CommandBar Header="项目文件">
                <Button Content="新建" controls:ButtonAssist.IsPrimary="True"/>
                <Button Content="刷新"/>
                <controls:CommandBar.SecondaryContent>
                    <Button Content="更多"/>
                </controls:CommandBar.SecondaryContent>
            </controls:CommandBar>
            """,
            [P("Header", "object", "命令栏标题或自定义标题内容"), P("Items", "ItemCollection", "主要命令集合"), P("SecondaryContent", "object", "靠右显示的辅助内容")],
            ["最常用命令放在最左侧", "紧凑场景可设置 IsCompact"],
            ["命令栏", "commandbar", "toolbar"]),

        Item(
            "expander", "Expander", "System.Windows.Controls.Expander", "布局与内容", "⌄",
            "按需展开次要设置或说明内容。",
            "统一 Expander 使用圆角表面、旋转箭头和悬停反馈，适合高级配置、详细日志和分组说明。",
            """
            <Expander Header="高级设置" IsExpanded="True">
                <TextBlock Margin="0,10,0,0"
                           Text="仅在需要覆盖默认行为时修改。"/>
            </Expander>
            """,
            [P("Header", "object", "始终可见的标题"), P("IsExpanded", "bool", "展开状态"), P("Content", "object", "折叠的详细内容")],
            ["关键操作不应默认折叠", "大量内容内部可再放置 ScrollViewer"],
            ["折叠", "expander", "高级设置"]),

        Item(
            "image", "Image", "System.Windows.Controls.Image", "布局与内容", "▧",
            "显示位图资源，并提供统一的缩放和圆角方案。",
            "统一 Image 默认启用高质量缩放；使用 UnifiedRoundedImageStyle 可快速获得与工具卡片一致的圆角裁剪。",
            """
            <Image Width="160" Height="96"
                   Source="/Resources/Image/default_tool_icon.png"
                   Style="{StaticResource UnifiedRoundedImageStyle}"
                   Stretch="UniformToFill"/>
            """,
            [P("Source", "ImageSource", "图片资源、文件或网络缓存结果"), P("Stretch", "Stretch", "图片缩放策略"), P("Style", "Style", "可选 UnifiedRoundedImageStyle")],
            ["工具包图片建议使用 pack URI", "避免直接在 UI 线程加载大尺寸网络图片"],
            ["图片", "image", "圆角", "bitmap"]),

        Item(
            "person-picture", "PersonPicture", "controls:PersonPicture", "布局与内容", "人",
            "显示头像、姓名首字和在线状态。",
            "PersonPicture 与主窗口左下角个人头像使用同一视觉规范；无图片时会根据 DisplayName 自动生成首字。",
            """
            <controls:PersonPicture Width="52" Height="52"
                                    DisplayName="XFE 工作室室长"
                                    IsOnline="True"/>
            """,
            [P("ProfilePicture", "ImageSource", "可选头像图片"), P("DisplayName", "string", "用于生成默认首字的名称"), P("IsOnline", "bool", "是否显示在线状态点")],
            ["业务头像应提供有意义的 DisplayName", "Initials 可覆盖自动生成的首字"],
            ["头像", "person", "用户", "在线"]),

        Item(
            "carousel", "Carousel", "controls:Carousel", "布局与内容", "▣",
            "展示可自动轮播的推荐内容。",
            "Carousel 支持标题、徽标、分页指示、前后导航、加载状态和点击动作，适合首页推荐或教程入口。",
            """
            <controls:Carousel Height="300"
                               ImageList="{Binding Recommendations}"
                               AutoPlay="True"
                               Interval="0:0:6"/>
            """,
            [P("ImageList", "ObservableCollection<CarouselImageItem>", "轮播数据集合"), P("AutoPlay", "bool", "是否自动切换"), P("Interval", "TimeSpan", "自动切换间隔")],
            ["每项可通过 Action 响应点击", "加载失败时可启用 CanRetry 并处理 RetryRequested"],
            ["轮播图", "carousel", "推荐", "banner"]),

        Item(
            "data-grid", "DataGrid", "System.Windows.Controls.DataGrid", "数据与导航", "▦",
            "展示和编辑结构化表格数据。",
            "统一 DataGrid 覆盖表头、行、单元格、选择、编辑控件和滚动条，适合日志、扫描结果和批量任务列表。",
            """
            <DataGrid ItemsSource="{Binding Results}"
                      AutoGenerateColumns="False" IsReadOnly="True">
                <DataGridTextColumn Header="名称" Binding="{Binding Name}" Width="*"/>
                <DataGridTextColumn Header="状态" Binding="{Binding Status}" Width="120"/>
            </DataGrid>
            """,
            [P("ItemsSource", "IEnumerable", "表格行数据"), P("Columns", "ObservableCollection", "列定义"), P("IsReadOnly", "bool", "是否禁止单元格编辑")],
            ["大数据集合应使用虚拟化并避免复杂单元格模板", "数值列建议右对齐"],
            ["表格", "datagrid", "数据", "列表"]),

        Item(
            "tab-view", "TabView", "controls:TabView", "数据与导航", "═",
            "顶部页签式内容导航。",
            "TabView 让选中的页签与内容面板在视觉上自然连接；页签过多或窗口较窄时，标题区域可独立横向滚动。",
            """
            <controls:TabView>
                <TabItem Header="概览"><TextBlock Text="概览内容"/></TabItem>
                <TabItem Header="日志"><TextBlock Text="日志内容"/></TabItem>
                <TabItem Header="设置"><TextBlock Text="设置内容"/></TabItem>
            </controls:TabView>
            """,
            [P("Items", "ItemCollection", "TabItem 页面集合"), P("SelectedIndex", "int", "当前页签索引"), P("SelectedItem", "object", "当前页签")],
            ["适合 2–8 个同级页面", "页签标题应短且可区分"],
            ["顶部导航", "tab", "top", "分页"]),

        Item(
            "navigation-view", "NavigationView", "controls:NavigationView", "数据与导航", "☰",
            "带独立滚动区域的左侧导航分页。",
            "NavigationView 适合设置页或功能较多的工具，导航列表和当前子页分别滚动，避免长页面互相干扰。",
            """
            <controls:NavigationView NavigationWidth="180">
                <TabItem Header="常规"><TextBlock Text="常规设置"/></TabItem>
                <TabItem Header="网络"><TextBlock Text="网络设置"/></TabItem>
                <TabItem Header="高级"><TextBlock Text="高级设置"/></TabItem>
            </controls:NavigationView>
            """,
            [P("NavigationWidth", "GridLength", "左侧导航栏宽度"), P("Items", "ItemCollection", "子页面集合"), P("SelectedIndex", "int", "当前子页索引")],
            ["导航标题可以使用带图标的自定义内容", "窄窗口下注意为右侧内容保留足够宽度"],
            ["左侧导航", "navigation", "tab", "设置"]),

        Item(
            "command-preview", "CommandPreviewBox", "controls:CommandPreviewBox", "工具增强", ">_",
            "展示图形操作对应的命令并提供一键复制。",
            "CommandPreviewBox 用于让开发者确认工具即将执行的命令，控件本身不会启动进程，执行权限始终由调用方控制。",
            """
            <controls:CommandPreviewBox Label="等价命令"
                                        CommandText="dotnet publish -c Release --output ./artifacts"
                                        IsSyntaxHighlightingEnabled="True"
                                        CopyButtonText="复制"/>
            """,
            [P("Label", "string", "命令区说明"), P("CommandText", "string", "显示和复制的命令"), P("IsSyntaxHighlightingEnabled", "bool", "是否启用命令语法着色"), P("CopyButtonText", "string", "复制按钮文字")],
            ["不要把密码或令牌拼入可见命令", "执行命令前仍需独立校验参数"],
            ["命令", "command", "copy", "终端"]),

        Item(
            "scroll-text", "ScrollTextBlock", "controls:ScrollTextBlock", "工具增强", "↔",
            "单行溢出时自动或悬停滚动的文本。",
            "ScrollTextBlock 默认用省略号显示长文本，悬停或设置 AutoRolling 后平滑滚动，适合工具名称、路径和窄卡片标题。",
            """
            <controls:ScrollTextBlock Width="220"
                                      InnerText="很长的项目路径或任务名称"
                                      AutoRolling="True"
                                      RollingBack="True"/>
            """,
            [P("InnerText", "string", "要显示的单行文本"), P("AutoRolling", "bool", "加载后是否自动滚动"), P("RollingTimeMillisecond", "double", "单次滚动时长")],
            ["短文本不会启动无意义动画", "RollingBack 可让文本往返滚动"],
            ["滚动文本", "marquee", "ellipsis", "路径"]),

        Item(
            "mini-tool-button", "MiniToolButton", "controls:MiniToolButton", "工具增强", "✦",
            "主界面工具快捷入口使用的图标按钮。",
            "MiniToolButton 组合工具图标、滚动名称和可选进度条，支持 ICommand，可用于工具内部的快捷操作面板。",
            """
            <controls:MiniToolButton ToolName="局域网文件传输"
                                     IconSource="/Resources/Image/wrench_tool.png"
                                     ProgressVisibility="Visible"
                                     ProgressValue="64"
                                     Command="{Binding OpenToolCommand}"/>
            """,
            [P("ToolName", "string", "工具名称"), P("IconSource", "ImageSource", "工具图标"), P("ProgressValue", "double", "可选的任务进度")],
            ["深色背景上使用浅色 TextColor", "命令参数通过 CommandParameter 传递"],
            ["工具按钮", "mini", "shortcut", "progress"])
    ];

    private static ControlGalleryItem Item(
        string id,
        string name,
        string typeName,
        string category,
        string icon,
        string summary,
        string description,
        string xaml,
        IReadOnlyList<ControlGalleryProperty> properties,
        IReadOnlyList<string> notes,
        IReadOnlyList<string> keywords) =>
        new(id, name, typeName, category, icon, summary, description, xaml.Trim(), properties, notes, keywords);

    private static ControlGalleryProperty P(string name, string type, string description) => new(name, type, description);
}
