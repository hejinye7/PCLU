Public Class PageOtherTest

    Private Class TemurinPackage
        Public Major As Integer
        Public ReleaseName As String
        Public DownloadUrl As String
        Public Sha256 As String
        Public Size As Long
        Public ArchivePath As String
        Public InstallPath As String
    End Class

    Private Class UsbInstanceEntry
        Public Instance As McInstance
        Public CheckBox As MyCheckBox
    End Class

    Private UsbTargetPath As String = ""
    Private UsbEntries As New List(Of UsbInstanceEntry)
    Private UsbInitialized As Boolean = False
    Private UsbScanStarted As Boolean = False
    Private UsbScannedFolder As String = ""

    Private Sub PageOtherTest_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        LabRepairTitle.Text = If(McInstanceSelected Is Nothing, "当前未选择游戏版本", "修复当前版本：" & McInstanceSelected.Name)
        BtnRepair.IsEnabled = McInstanceSelected IsNot Nothing
        If UsbScanStarted AndAlso Not PathUtils.IsSame(McFolderSelected, UsbScannedFolder) Then UsbScanStarted = False
        InitUsbPanel()
    End Sub

    Private Sub InitUsbPanel()
        If Not UsbInitialized Then
            UsbInitialized = True
            ComboUsbMode.Items.Add(New MyComboBoxItem With {.Content = "便携启动器（批量实例）", .Tag = "launcher"})
            ComboUsbMode.Items.Add(New MyComboBoxItem With {.Content = "单实例启动 BAT", .Tag = "batch"})
            ComboUsbMode.SelectedIndex = 0
            RefreshUsbInstances()
        ElseIf UsbEntries.Count = 0 Then
            RefreshUsbInstances()
        End If
        If Not UsbScanStarted Then
            UsbScanStarted = True
            LabUsbStatus.Text = "正在扫描 Minecraft 文件夹和实例……"
            RunInNewThread(
            Sub()
                Try
                    McFolderListLoader.WaitForExit()
                    Dim Folder = McFolderSelected
                    If String.IsNullOrWhiteSpace(Folder) OrElse Not McFolderList.Any(Function(Item) PathUtils.IsSame(Item.Location, Folder)) Then
                        Folder = McFolderList.FirstOrDefault()?.Location
                        If Not String.IsNullOrWhiteSpace(Folder) Then McFolderSelected = Folder
                    End If
                    If String.IsNullOrWhiteSpace(Folder) Then Throw New Exception("没有可用的 Minecraft 文件夹。")
                    LoaderFolderRun(McInstanceListLoader, Folder, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\", WaitForExit:=True)
                    UsbScannedFolder = Folder
                    RunInUi(Sub()
                                If IsLoaded Then
                                    RefreshUsbInstances()
                                    LabUsbStatus.Text = If(UsbEntries.Count = 0, "当前 Minecraft 文件夹中没有可用实例。", "实例扫描完成。")
                                End If
                            End Sub)
                Catch ex As Exception
                    Logger.Error(ex, "扫描 U 盘导出实例失败")
                    RunInUi(Sub()
                                If IsLoaded Then
                                    UsbScanStarted = False
                                    LabUsbStatus.Text = "扫描失败：" & ex.Message
                                End If
                            End Sub)
                End Try
            End Sub, "U 盘实例扫描", ThreadPriority.BelowNormal)
        End If
    End Sub

    Private Sub RefreshUsbInstances()
        If Not UsbInitialized Then Return
        UsbEntries.Clear()
        PanUsbInstances.Children.Clear()
        Dim AllInstances As New List(Of McInstance)
        For Each Group In McInstanceList.Values
            For Each Instance In Group
                If Instance IsNot Nothing AndAlso Instance.State <> McInstance.McInstanceState.Error AndAlso
                   Not AllInstances.Any(Function(Item) PathUtils.IsSame(Item.PathVersion, Instance.PathVersion)) Then AllInstances.Add(Instance)
            Next
        Next
        For Each Instance In AllInstances.OrderBy(Function(Item) Item.Name)
            Dim Check As New MyCheckBox With {.Text = Instance.Name, .Margin = New Thickness(0, 2, 0, 2), .Tag = Instance}
            Check.Checked = (McInstanceSelected IsNot Nothing AndAlso PathUtils.IsSame(Instance.PathVersion, McInstanceSelected.PathVersion))
            AddHandler Check.Change, Sub(CheckSender As Object, UserChange As Boolean) UpdateUsbExportState()
            PanUsbInstances.Children.Add(Check)
            UsbEntries.Add(New UsbInstanceEntry With {.Instance = Instance, .CheckBox = Check})
        Next
        If AllInstances.Count = 0 Then
            PanUsbInstances.Children.Add(New TextBlock With {.Text = "当前 Minecraft 文件夹中没有可用实例。", .Opacity = 0.55, .Margin = New Thickness(0, 4, 0, 0)})
        End If
        UpdateUsbExportState()
    End Sub

    Private Function UsbMode() As String
        Dim Item = TryCast(ComboUsbMode.SelectedItem, MyComboBoxItem)
        Return If(Item Is Nothing, "launcher", CStr(Item.Tag))
    End Function

    Private Function SelectedUsbInstances() As List(Of McInstance)
        Return UsbEntries.Where(Function(Entry) Entry.CheckBox.Checked).Select(Function(Entry) Entry.Instance).ToList()
    End Function

    Private Sub UpdateUsbExportState()
        BtnUsbExport.IsEnabled = UsbTargetPath <> "" AndAlso SelectedUsbInstances().Count > 0
        If UsbTargetPath = "" Then
            LabUsbStatus.Text = "请选择目标 U 盘。"
        ElseIf SelectedUsbInstances().Count = 0 Then
            LabUsbStatus.Text = "请至少选择一个游戏实例。"
        Else
            LabUsbStatus.Text = $"已选择 {SelectedUsbInstances().Count} 个实例，准备写入 {UsbTargetPath}"
        End If
    End Sub

    Private Sub ComboUsbMode_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboUsbMode.SelectionChanged
        If Not UsbInitialized Then Return
        If UsbMode() = "batch" AndAlso SelectedUsbInstances().Count > 1 Then
            Dim First = SelectedUsbInstances().First()
            For Each Entry In UsbEntries
                Entry.CheckBox.Checked = PathUtils.IsSame(Entry.Instance.PathVersion, First.PathVersion)
            Next
        End If
        BtnUsbExport.Text = If(UsbMode() = "batch", "导出实例 BAT", "安装到 U 盘")
        UpdateUsbExportState()
    End Sub

    Private Sub BtnUsbSelect_Click(sender As Object, e As EventArgs) Handles BtnUsbSelect.Click
        Dim Selected = Dialogs.SelectFolder("选择 U 盘根目录（不要选择 .minecraft 文件夹）", False).FirstOrDefault()
        If String.IsNullOrWhiteSpace(Selected) Then Return
        UsbTargetPath = PathUtils.RemoveSlashSuffix(PathUtils.ForCompare(Selected))
        LabUsbTarget.Text = UsbTargetPath
        UpdateUsbExportState()
    End Sub

    Private Sub BtnUsbExport_Click(sender As Object, e As EventArgs) Handles BtnUsbExport.Click
        Dim Target = UsbTargetPath
        Dim Instances = SelectedUsbInstances()
        Dim SingleBatch = UsbMode() = "batch"
        If Target = "" OrElse Instances.Count = 0 Then
            UpdateUsbExportState()
            Return
        End If
        If SingleBatch Then Instances = New List(Of McInstance) From {Instances.First()}
        Dim LegacyName = If(Settings.Get(Of McLoginType)("LoginType") = McLoginType.Legacy, CType(PageLoginLegacy.GetLoginData(), McLoginLegacy).UserName, "")
        If PathUtils.IsSame(Target, PathUtils.RemoveSlashSuffix(McFolderSelected)) OrElse Target.StartsWith(PathUtils.RemoveSlashSuffix(McFolderSelected) & "\", StringComparison.OrdinalIgnoreCase) Then
            Hint("U 盘目标不能位于当前 Minecraft 文件夹内。", HintType.Red)
            Return
        End If
        BtnUsbExport.IsEnabled = False
        BtnUsbSelect.IsEnabled = False
        RunInNewThread(
        Sub()
            Try
                PortableExport(Target, Instances, SingleBatch, LegacyName)
            Catch ex As Exception
                Logger.Error(ex, "U 盘便携安装失败")
                RunInUi(Sub() LabUsbStatus.Text = "安装失败：" & ex.Message)
                Hint("U 盘便携安装失败：" & ex.Message, HintType.Red)
            Finally
                RunInUi(Sub()
                            BtnUsbSelect.IsEnabled = True
                            UpdateUsbExportState()
                        End Sub)
            End Try
        End Sub, "U 盘便携安装", ThreadPriority.BelowNormal)
        Hint("已开始安装到 U 盘，进度会显示在这里。", HintType.Green)
    End Sub

    Private Shared Sub PortableStatus(Text As String)
        RunInUi(Sub()
                    If FrmMain?.PageRight IsNot Nothing AndAlso TypeOf FrmMain.PageRight Is PageOtherTest Then
                        CType(FrmMain.PageRight, PageOtherTest).LabUsbStatus.Text = Text
                    End If
                End Sub)
    End Sub

    Private Shared Function VersionDependencies(SourceMc As String, Selected As IEnumerable(Of McInstance)) As HashSet(Of String)
        Dim Result As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim Pending As New Queue(Of String)(Selected.Select(Function(Item) Item.Name))
        While Pending.Count > 0
            Dim Name = Pending.Dequeue()
            If Not Result.Add(Name) Then Continue While
            Dim JsonPath = Path.Combine(SourceMc, "versions", Name, Name & ".json")
            Try
                Dim Inherit = FileUtils.ReadAsJson(JsonPath)("inheritsFrom")?.ToString()
                If Not String.IsNullOrWhiteSpace(Inherit) Then Pending.Enqueue(Inherit)
            Catch
            End Try
        End While
        Return Result
    End Function

    Private Shared Sub RemoveUnselectedVersions(TargetMc As String, Keep As HashSet(Of String))
        Dim VersionsPath = Path.Combine(TargetMc, "versions")
        If Not Directory.Exists(VersionsPath) Then Return
        For Each Folder In Directory.GetDirectories(VersionsPath)
            If Not Keep.Contains(Path.GetFileName(Folder)) Then DirectoryUtils.Delete(Folder)
        Next
    End Sub

    Private Shared Function PortablePath(Text As String, SourceMc As String, SourcePure As String) As String
        If Text Is Nothing Then Return ""
        Dim Result = Text
        For Each Source In {SourceMc, PathUtils.ToShortPath(SourceMc), SourcePure, PathUtils.ToShortPath(SourcePure)}.Distinct().OrderByDescending(Function(Item) Item.Length)
            If Not String.IsNullOrEmpty(Source) Then Result = Result.Replace(Source.TrimEnd("\"c), "%~dp0" & If(PathUtils.IsSame(Source, SourcePure), "PCL", ".minecraft"))
        Next
        Return Result
    End Function

    Private Shared Sub WritePortableLauncher(Target As String, LegacyName As String, IncludeJava As Boolean)
        Dim LauncherPath = Path.Combine(Target, "PCLU.exe")
        FileUtils.Copy(PathExe, LauncherPath)
        Dim Bat = "@echo off" & vbCrLf & "cd /d ""%~dp0""" & vbCrLf &
                  "start ""PCLU"" ""%~dp0PCLU.exe"" --portable-minecraft ""%~dp0.minecraft\"""
        If IncludeJava Then Bat &= " --portable-java ""%~dp0java\"""
        If LegacyName <> "" Then Bat &= " --portable-legacy """ & LegacyName.Replace("""", "") & """"
        Bat &= vbCrLf
        FileUtils.Write(Path.Combine(Target, "PCLU-便携启动器.bat"), Bat)
    End Sub

    Private Sub PortableExport(Target As String, Instances As List(Of McInstance), SingleBatch As Boolean, LegacyName As String)
        Dim SourceMc = PathUtils.AddSlashSuffix(McFolderSelected)
        Dim TargetMc = Path.Combine(Target, ".minecraft") & "\"
        PortableStatus("正在复制 Minecraft 文件和共享资源……")
        If Directory.Exists(TargetMc) Then DirectoryUtils.Delete(TargetMc)
        DirectoryUtils.Copy(SourceMc, TargetMc)
        RemoveUnselectedVersions(TargetMc, VersionDependencies(SourceMc, Instances))
        WriteIni(TargetMc & "PCL.ini", "Version", Instances.First().Name)
        WriteIni(TargetMc & "PCL.ini", "InstanceCache", "")
        PortableStatus("正在复制启动器文件……")
        DirectoryUtils.Copy(PathPure.Value, Path.Combine(Target, "PCL") & "\")
        Dim JavaToCopy = McLaunchJavaSelected
        If JavaToCopy Is Nothing Then JavaToCopy = Configs.JavaList.Get().FirstOrDefault()
        If JavaToCopy IsNot Nothing AndAlso Directory.Exists(JavaToCopy.Folder) Then
            PortableStatus("正在复制 Java 运行时……")
            Dim JavaTarget = Path.Combine(Target, "java") & "\"
            If Directory.Exists(JavaTarget) Then DirectoryUtils.Delete(JavaTarget)
            DirectoryUtils.Copy(JavaToCopy.Folder, JavaTarget)
        End If
        If Not SingleBatch Then
            WritePortableLauncher(Target, LegacyName, JavaToCopy IsNot Nothing AndAlso Directory.Exists(JavaToCopy.Folder))
            PortableStatus("便携启动器已完成，可运行 PCLU-便携启动器.bat。")
            Hint("U 盘便携启动器安装完成！", HintType.Green)
            Return
        End If

        Dim TempFolder = RequestTaskTempFolder()
        Dim TempBat = Path.Combine(TempFolder, "portable-launch.bat")
        Dim Started As Boolean = False
        RunInUiWait(Sub() Started = McLaunchStart(New McLaunchOptions With {.SaveBatch = TempBat, .Instance = Instances.First()}))
        If Not Started Then Throw New Exception("启动参数生成失败，请先确认该实例可以正常启动。")
        Dim WaitStart = Date.Now
        Do While McLaunchLoader.State = LoadState.Loading
            If (Date.Now - WaitStart).TotalMinutes > 5 Then Throw New TimeoutException("等待启动参数生成超时")
            Thread.Sleep(100)
        Loop
        If Not FileUtils.Exists(TempBat) Then Throw New FileNotFoundException("没有生成启动脚本")
        PortableStatus("正在整理 Java 运行时和相对路径……")
        If McLaunchJavaSelected Is Nothing Then Throw New Exception("没有找到可用的 Java")
        Dim JavaTargetBatch = Path.Combine(Target, "java") & "\"
        If Directory.Exists(JavaTargetBatch) Then DirectoryUtils.Delete(JavaTargetBatch)
        DirectoryUtils.Copy(McLaunchJavaSelected.Folder, JavaTargetBatch)
        Dim Content = File.ReadAllText(TempBat, Encoding.Default)
        Content = PortablePath(Content, SourceMc, PathPure.Value)
        Dim JavaExe = McLaunchJavaSelected.JavaExePath
        Content = Content.Replace(JavaExe, "%~dp0java\bin\java.exe")
        If Settings.Get(Of McLoginType)("LoginType") = McLoginType.Legacy AndAlso McLoginLoader.Output.AccessToken <> "" Then
            Dim Masked = FilterAccessToken(McLoginLoader.Output.AccessToken, "F"c)
            Content = Content.Replace(Masked, McLoginLoader.Output.AccessToken)
        End If
        Dim BatchPath = Path.Combine(Target, "启动 " & Instances.First().Name & ".bat")
        FileUtils.Write(filePath:=BatchPath, text:=Content, encoding:=Encoding.Default)
        DirectoryUtils.Delete(TempFolder)
        PortableStatus("单实例启动 BAT 已完成，可在 U 盘根目录运行。")
        Hint("U 盘单实例启动 BAT 导出完成！", HintType.Green)
    End Sub

    Private Sub BtnRepair_Click(sender As Object, e As EventArgs) Handles BtnRepair.Click
        Dim Instance As McInstance = McInstanceSelected
        If Instance Is Nothing Then
            Hint("请先在启动页选择一个游戏版本！", HintType.Red)
            Return
        End If
        Try
            If McInstanceLock.IsLocked(Instance) Then
                Hint("该版本正在被修改，请稍后再修复！", HintType.Red)
                Return
            End If
            If ShouldIgnoreFileCheck(Instance) Then
                Hint("请先关闭该版本高级启动选项中的文件校验忽略设置！", HintType.Blue)
                Return
            End If
            Dim TaskName As String = Instance.Name & " 文件修复"
            If LoaderTaskbar.Any(Function(Task) Task.Name = TaskName AndAlso Task.State = LoadState.Loading) Then
                Hint("该版本正在修复中！", HintType.Red)
                Return
            End If
            If Instance.State = McInstance.McInstanceState.Error AndAlso
               Instance.Info.IndexOf("JSON", StringComparison.OrdinalIgnoreCase) >= 0 Then
                StartJsonRecovery(Instance)
            Else
                StartRepair(Instance)
            End If
        Catch ex As Exception
            Logger.Error(ex, "启动游戏文件修复失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub StartRepair(Instance As McInstance)
        Try
            Dim TaskName As String = Instance.Name & " 文件修复"
            Dim Loader As New LoaderCombo(Of String)(TaskName, DlClientFix(Instance, True, False))
            Loader.OnStateChanged =
            Sub()
                Select Case Loader.State
                    Case LoadState.Finished
                        Hint(TaskName & "完成！", HintType.Green)
                        Dim RefreshedInstance = New McInstance(Instance.PathVersion).Load()
                        RunInUi(
                        Sub()
                            If McInstanceSelected IsNot Nothing AndAlso PathUtils.IsSame(McInstanceSelected.PathVersion, Instance.PathVersion) Then
                                McInstanceSelected = RefreshedInstance
                                LabRepairTitle.Text = "修复当前版本：" & RefreshedInstance.Name
                                If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshButtonsUI()
                            End If
                        End Sub)
                        WriteIni(McFolderSelected & "PCL.ini", "InstanceCache", "")
                        LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
                    Case LoadState.Failed
                        Hint(TaskName & "失败：" & Loader.Error.GetDisplay(False), HintType.Red)
                    Case LoadState.Canceled
                        Hint(TaskName & "已取消！", HintType.Blue)
                End Select
            End Sub
            Loader.Start(Instance.Name)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
            Hint("已开始校验并修复 " & Instance.Name, HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "启动游戏文件修复失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub StartJsonRecovery(Instance As McInstance)
        Dim TaskName As String = Instance.Name & " 文件修复：恢复版本 JSON"
        If LoaderTaskbar.Any(Function(Task) Task.Name = TaskName AndAlso Task.State = LoadState.Loading) Then
            Hint("该版本正在恢复 JSON！", HintType.Red)
            Return
        End If

        Dim TempFolder As String = RequestTaskTempFolder()
        Dim TempJson As String = TempFolder & Instance.Name & ".json"
        Dim TargetJson As String = Instance.PathVersion & Instance.Name & ".json"
        Dim BackupJson As String = TargetJson & ".pcll-backup"
        Dim HadOriginal As Boolean = FileUtils.Exists(TargetJson)
        Dim RestoredInstance As McInstance = Nothing
        Dim Loaders As New List(Of LoaderBase) From {
            New LoaderTask(Of String, List(Of NetFile))("查找原版版本 JSON",
            Sub(Task)
                Dim JsonAddress As String = DlClientListGet(Instance.Name)
                If String.IsNullOrEmpty(JsonAddress) Then
                    Throw New Exception("官方版本清单中没有 " & Instance.Name & "。该版本可能是 Mod 加载器版本，请重新安装对应版本。")
                End If
                Task.Output = New List(Of NetFile) From {
                    New NetFile(DlSourceLauncherOrMetaGet(JsonAddress), TempJson, New FileChecker With {.IsJson = True})
                }
            End Sub) With {.ProgressWeight = 1},
            New LoaderDownload("下载原版版本 JSON", New List(Of NetFile)) With {.ProgressWeight = 2},
            New LoaderTask(Of List(Of NetFile), Boolean)("应用并验证版本 JSON",
            Sub()
                Dim DownloadedJson As JObject = FileUtils.ReadAsJson(TempJson)
                If DownloadedJson("mainClass") Is Nothing Then Throw New Exception("下载的版本 JSON 缺少 mainClass")
                If HadOriginal Then FileUtils.Copy(TargetJson, BackupJson)
                FileUtils.Copy(TempJson, TargetJson)
                RestoredInstance = New McInstance(Instance.PathVersion).Load()
                If RestoredInstance.State = McInstance.McInstanceState.Error Then
                    If HadOriginal Then
                        FileUtils.Copy(BackupJson, TargetJson)
                    Else
                        FileUtils.Delete(TargetJson)
                    End If
                    Throw New Exception("恢复后的版本 JSON 仍无法使用：" & RestoredInstance.Info)
                End If
            End Sub) With {.ProgressWeight = 1}
        }

        Dim Loader As New LoaderCombo(Of String)(TaskName, Loaders)
        Loader.OnStateChanged =
        Sub()
            Select Case Loader.State
                Case LoadState.Finished
                    RunInUi(
                    Sub()
                        McInstanceSelected = RestoredInstance
                        LabRepairTitle.Text = "修复当前版本：" & RestoredInstance.Name
                        If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.RefreshButtonsUI()
                        StartRepair(RestoredInstance)
                    End Sub)
                Case LoadState.Failed
                    Hint(TaskName & "失败：" & Loader.Error.GetDisplay(False), HintType.Red)
                Case LoadState.Canceled
                    Hint(TaskName & "已取消！", HintType.Blue)
            End Select
        End Sub
        Loader.Start(Instance.Name)
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        Hint("检测到版本 JSON 损坏，正在自动恢复……", HintType.Blue)
    End Sub

    Private Shared Function CacheFolders() As String()
        Return {
            PathTemp & "Download\",
            PathTemp & "TaskTemp\",
            PathTemp & "Cache\Http\"
        }
    End Function

    Private Shared Sub ScanCache(ByRef FileCount As Integer, ByRef TotalSize As Long)
        For Each Folder In CacheFolders()
            If Not DirectoryUtils.Exists(Folder) Then Continue For
            For Each FilePath In DirectoryUtils.EnumerateFiles(Folder, True)
                Try
                    TotalSize += FileUtils.GetInfo(FilePath).Length
                    FileCount += 1
                Catch
                End Try
            Next
        Next
    End Sub

    Private Sub BtnCacheClear_Click(sender As Object, e As EventArgs) Handles BtnCacheClear.Click
        If LoaderTaskbar.Any(Function(Task) Task.State = LoadState.Loading) Then
            Hint("请等待当前下载或安装任务结束后再清理缓存！", HintType.Red)
            Return
        End If
        BtnCacheClear.IsEnabled = False
        RunInNewThread(
        Sub()
            Try
                Dim FileCount As Integer = 0
                Dim TotalSize As Long = 0
                ScanCache(FileCount, TotalSize)
                If FileCount = 0 Then
                    Hint("没有可清理的下载残片或临时缓存。", HintType.Blue)
                    Return
                End If
                Dim Choice = RunInUiWait(Function() MyMsgBox(
                    $"共找到 {FileCount} 个缓存文件，占用 {StringUtils.FormatByteSize(TotalSize)}。{vbCrLf}{vbCrLf}确定要清理吗？",
                    "缓存清理", "清理", "取消"))
                If Choice <> 1 Then Return
                Dim FailedFolders As New List(Of String)
                For Each Folder In CacheFolders()
                    Try
                        If DirectoryUtils.Exists(Folder) Then DirectoryUtils.Delete(Folder)
                        DirectoryUtils.Create(Folder)
                    Catch ex As Exception
                        FailedFolders.Add(Folder)
                        Logger.Warn(ex, "清理缓存目录失败：" & Folder)
                    End Try
                Next
                If FailedFolders.Any Then
                    Hint("部分正在使用的缓存未能清理，其余内容已清理完成。", HintType.Blue)
                Else
                    Hint($"已清理 {FileCount} 个文件，释放约 {StringUtils.FormatByteSize(TotalSize)}！", HintType.Green)
                End If
            Catch ex As Exception
                Logger.Error(ex, "清理缓存失败", LogBehavior.Alert)
            Finally
                RunInUi(Sub() BtnCacheClear.IsEnabled = True)
            End Try
        End Sub, "安全缓存清理", ThreadPriority.BelowNormal)
    End Sub

    Private Sub BtnTemurinInstall_Click(sender As Object, e As EventArgs) Handles BtnTemurinInstall.Click
        Const TaskName As String = "安装 Eclipse Temurin JDK"
        If LoaderTaskbar.Any(Function(Task) Task.Name = TaskName AndAlso Task.State = LoadState.Loading) Then
            Hint("Temurin JDK 正在安装中！", HintType.Blue)
            Return
        End If
        Dim TempFolder = RequestTaskTempFolder()
        Dim Packages As New List(Of TemurinPackage)
        Dim Loaders As New List(Of LoaderBase) From {
            New LoaderTask(Of String, List(Of NetFile))("获取 Temurin 官方清单",
            Sub(Task)
                Dim Files As New List(Of NetFile)
                For Each Major In {8, 11, 17, 21, 25}
                    Dim Api = $"https://api.adoptium.net/v3/assets/latest/{Major}/hotspot?architecture=x64&image_type=jdk&os=windows&vendor=eclipse"
                    Dim Assets = NetRequestByClientRetry(Api, RequireJson:=True).DeserializeJson(Of JArray)()
                    Dim Asset = TryCast(Assets.FirstOrDefault(), JObject)
                    Dim Package = TryCast(Asset?("binary")?("package"), JObject)
                    If Package Is Nothing Then Throw New Exception($"Adoptium 没有返回 Temurin JDK {Major} 的 Windows x64 安装包。")
                    Dim Entry As New TemurinPackage With {
                        .Major = Major,
                        .ReleaseName = Asset("release_name")?.ToString(),
                        .DownloadUrl = Package("link")?.ToString(),
                        .Sha256 = Package("checksum")?.ToString(),
                        .Size = Package("size")?.ToObject(Of Long)(),
                        .ArchivePath = Path.Combine(TempFolder, $"temurin-jdk-{Major}.zip")
                    }
                    Dim SafeRelease = Entry.ReleaseName.RegexReplace("[^A-Za-z0-9._-]", "_")
                            Entry.InstallPath = Path.Combine(Paths.Base & ".minecraft\runtime", $"temurin-jdk-{Major}-{SafeRelease}")
                    Packages.Add(Entry)
                    Files.Add(New NetFile({Entry.DownloadUrl}, Entry.ArchivePath,
                        New FileChecker With {.ActualSize = Entry.Size, .Hash = Entry.Sha256}))
                Next
                Task.Output = Files
            End Sub) With {.ProgressWeight = 1},
            New LoaderDownload("下载 Temurin JDK", New List(Of NetFile)) With {.ProgressWeight = 20},
            New LoaderTask(Of List(Of NetFile), Boolean)("静默安装 Temurin JDK",
            Sub(Task)
                For Index = 0 To Packages.Count - 1
                    Dim Package = Packages(Index)
                    DirectoryUtils.Create(Package.InstallPath)
                    FileUtils.ExtractToDirectory(Package.ArchivePath, Package.InstallPath, Task.CreateCancellationToken())
                    If Not DirectoryUtils.EnumerateFiles(Package.InstallPath, True).
                        Any(Function(FilePath) Path.GetFileName(FilePath).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase)) Then
                        Throw New Exception($"Temurin JDK {Package.Major} 解压后未找到 javaw.exe。")
                    End If
                    Task.Progress = (Index + 1) / Packages.Count
                Next
                Task.Output = True
            End Sub) With {.ProgressWeight = 8},
            New LoaderWorker("刷新 Java 列表", JavaListRefreshWorker) With {.ProgressWeight = 2}
        }
        Dim Loader As New LoaderCombo(Of String)(TaskName, Loaders)
        Loader.OnStateChanged =
        Sub()
            Select Case Loader.State
                Case LoadState.Finished
                    RunInUi(Sub()
                        BtnTemurinInstall.IsEnabled = True
                        LabTemurinStatus.Text = "Temurin JDK 8、11、17、21、25 已安装并加入 PCL Java 列表。"
                    End Sub)
                    DirectoryUtils.Delete(TempFolder)
                    Hint("全部 Temurin JDK 安装完成！", HintType.Green)
                Case LoadState.Failed
                    DirectoryUtils.Delete(TempFolder)
                    RunInUi(Sub()
                        BtnTemurinInstall.IsEnabled = True
                        LabTemurinStatus.Text = "安装失败：" & Loader.Error.GetDisplay(False)
                    End Sub)
                    Hint("Temurin JDK 安装失败：" & Loader.Error.GetDisplay(False), HintType.Red)
                Case LoadState.Canceled
                    DirectoryUtils.Delete(TempFolder)
                    RunInUi(Sub()
                        BtnTemurinInstall.IsEnabled = True
                        LabTemurinStatus.Text = "安装已取消，可重新开始。"
                    End Sub)
            End Select
        End Sub
        BtnTemurinInstall.IsEnabled = False
        LabTemurinStatus.Text = "正在获取 Adoptium 官方清单，进度可在下载任务中查看。"
        Loader.Start(TaskName)
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        Hint("已开始安装全部 Temurin JDK。", HintType.Green)
    End Sub

    Public Shared Sub StartCustomDownload(Url As String, FileName As String, Optional Folder As String = Nothing)
        If String.IsNullOrWhiteSpace(Url) OrElse
           (Not Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) AndAlso Not Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) Then
            Hint("下载地址无效。", HintType.Red)
            Return
        End If
        Dim SafeName = Path.GetFileName(If(FileName, "download.bin"))
        If String.IsNullOrWhiteSpace(SafeName) Then SafeName = "download.bin"
        Dim TargetFolder = If(String.IsNullOrWhiteSpace(Folder), Paths.Base & "PCL\Downloads\", Folder)
        If Not Path.IsPathRooted(TargetFolder) Then TargetFolder = Path.Combine(Paths.Base, TargetFolder)
        Dim TargetPath = Path.Combine(TargetFolder, SafeName)
        Dim DownloadLoader As New LoaderDownload("自定义下载：" & SafeName,
                                                 New List(Of NetFile) From {New NetFile({Url}, TargetPath)})
        Dim Loader As New LoaderCombo(Of String)("自定义下载：" & SafeName,
                                                  New List(Of LoaderBase) From {DownloadLoader})
        Loader.Start()
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        Hint("已开始下载：" & SafeName, HintType.Green)
    End Sub
    Public Shared Sub Jrrp()
        Dim Score = Math.Abs(Date.Today.Ticks.GetHashCode()) Mod 101
        Hint($"今日人品：{Score}%", If(Score >= 80, HintType.Green, If(Score <= 20, HintType.Red, HintType.Blue)))
    End Sub
    Public Shared Sub RubbishClear()
        Dim Folders = {PathTemp & "Download\", PathTemp & "TaskTemp\", PathTemp & "Cache\Http\"}
        Dim Deleted As Integer = 0
        For Each Folder In Folders
            Try
                If DirectoryUtils.Exists(Folder) Then
                    Deleted += DirectoryUtils.EnumerateFiles(Folder, True).Count
                    DirectoryUtils.Delete(Folder)
                End If
                DirectoryUtils.Create(Folder)
            Catch ex As Exception
                Logger.Warn(ex, "清理临时文件失败：" & Folder)
            End Try
        Next
        Hint($"已清理 {Deleted} 个临时文件。", HintType.Green)
    End Sub
    Public Shared Sub MemoryOptimize(ShowHint As Boolean)
        MemoryOptimizeInternal(False)
        If ShowHint Then Hint("内存整理完成。", HintType.Green)
    End Sub
    Public Shared Sub MemoryOptimizeInternal(ShowHint As Boolean)
        Try
            GC.Collect()
            GC.WaitForPendingFinalizers()
            GC.Collect()
            Using Current = Process.GetCurrentProcess()
                Current.MinWorkingSet = Current.MinWorkingSet
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "整理启动器内存失败")
        End Try
        If ShowHint Then Hint("内存整理完成。", HintType.Green)
    End Sub
    Public Shared Function GetRandomCave() As String
        Return RandomOne(New List(Of String) From {"挖矿时记得带水桶。", "今天适合探索下界。", "别忘了备份存档。", "村民交易前先锁定价格。"})
    End Function
    Public Shared Function GetRandomHint() As String
        Return RandomOne(New List(Of String) From {"正在准备游戏环境……", "愿你的出生点附近就有村庄。", "启动后可以按 F11 切换全屏。", "记得给重要世界做好备份。"})
    End Function
    Public Shared Function GetRandomPresetHint() As String
        Return GetRandomHint()
    End Function

End Class
