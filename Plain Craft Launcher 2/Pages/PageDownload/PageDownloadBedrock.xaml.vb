Imports System.IO.Compression
Imports System.Text
Imports System.Xml.Linq
Imports Newtonsoft.Json.Linq

Public Class PageDownloadBedrock

    Private Const VersionManifestUrl As String = "https://data.mcappx.com/v2/bedrock.json"
    Private IsLoading As Boolean
    Private VersionEntries As New List(Of BedrockVersionEntry)

    Private Class BedrockVersionEntry
        Public Version As String
        Public Channel As String
        Public BuildType As String
        Public ReleaseDate As String
        Public Metadata As String
        Public Md5 As String

        Public Overrides Function ToString() As String
            Return Version & If(String.IsNullOrWhiteSpace(ReleaseDate), "", "  ·  " & ReleaseDate)
        End Function
    End Class

    Private Async Sub PageDownloadBedrock_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If ComboChannel.Items.Count = 0 Then
            ComboChannel.Items.Add(New MyComboBoxItem With {.Content = "正式版", .Tag = "Release"})
            ComboChannel.Items.Add(New MyComboBoxItem With {.Content = "预览版", .Tag = "Preview"})
            ComboChannel.SelectedIndex = 0
        End If
        If VersionEntries.Count = 0 Then Await LoadVersionsAsync()
    End Sub

    Private Sub ComboChannel_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboChannel.SelectionChanged
        If IsLoading OrElse Not IsLoaded Then Return
        FillVersionList()
    End Sub

    Private Async Sub Refresh_Click(sender As Object, e As MouseButtonEventArgs)
        Await LoadVersionsAsync()
    End Sub

    Private Async Function LoadVersionsAsync() As Task
        If IsLoading Then Return
        IsLoading = True
        BtnRefresh.IsEnabled = False
        BtnInstall.IsEnabled = False
        LabStatus.Text = "正在读取 MCAPPX 基岩版索引……"
        Try
            Dim Manifest As JObject = Nothing
            Await Task.Run(Sub()
                               Manifest = NetRequestByClientRetry(VersionManifestUrl, RequireJson:=True, SimulateBrowserHeaders:=True).DeserializeJson(Of JObject)()
                           End Sub)
            Dim NewEntries As New List(Of BedrockVersionEntry)
            For Each SourceGroup As JProperty In Manifest.Properties().Where(Function(JsonProperty) JsonProperty.Name <> "CreationTime")
                Dim Builds = TryCast(SourceGroup.Value, JObject)
                If Builds Is Nothing Then Continue For
                For Each Build As JProperty In Builds.Properties()
                    Dim Info = TryCast(Build.Value, JObject)
                    If Info Is Nothing Then Continue For
                    Dim Channel = Info("Type")?.ToString()
                    If Channel <> "Release" AndAlso Channel <> "Preview" Then Continue For
                    Dim BuildType = Info("BuildType")?.ToString()
                    If BuildType <> "UWP" Then Continue For
                    Dim Variations = TryCast(Info("Variations"), JArray)
                    If Variations Is Nothing Then Continue For
                    Dim X64 = Variations.Children(Of JObject)().FirstOrDefault(Function(Item) Item("Arch")?.ToString().Equals("x64", StringComparison.OrdinalIgnoreCase))
                    If X64 Is Nothing OrElse X64("ArchivalStatus")?.Value(Of Integer)() <> 3 Then Continue For
                    Dim Metadata = TryCast(X64("MetaData"), JArray)
                    Dim PackageMetadata = Metadata?.Values(Of String)().LastOrDefault()
                    If String.IsNullOrWhiteSpace(PackageMetadata) Then Continue For
                    NewEntries.Add(New BedrockVersionEntry With {
                        .Version = Build.Name,
                        .Channel = Channel,
                        .BuildType = BuildType,
                        .ReleaseDate = Info("Date")?.ToString(),
                        .Metadata = PackageMetadata,
                        .Md5 = X64("MD5")?.ToString()
                    })
                Next
            Next
            VersionEntries = NewEntries.OrderByDescending(Function(Entry) VersionSortKey(Entry.Version)).ToList()
            FillVersionList()
            LabStatus.Text = $"已读取 {VersionEntries.Count} 个可静默部署的 x64 UWP 版本。安装包将保留在 .minecraft\bedrock\packages。"
        Catch ex As Exception
            ComboVersion.Items.Clear()
            LabStatus.Text = "读取基岩版索引失败：" & ex.GetDisplay(False)
            Logger.Error(ex, "读取基岩版版本索引失败")
        Finally
            IsLoading = False
            BtnRefresh.IsEnabled = True
        End Try
    End Function

    Private Shared Function VersionSortKey(Value As String) As Long
        Dim Parts = Value.Split("."c).Select(Function(Part)
                                                  Dim Number As Integer
                                                  Integer.TryParse(Part.RegexSeek("[0-9]+"), Number)
                                                  Return Math.Min(Number, 9999)
                                              End Function).Concat({0, 0, 0, 0}).Take(4).ToArray()
        Return CLng(Parts(0)) * 1000000000000L + CLng(Parts(1)) * 100000000L + CLng(Parts(2)) * 10000L + Parts(3)
    End Function

    Private Sub FillVersionList()
        ComboVersion.Items.Clear()
        If ComboChannel.SelectedItem Is Nothing Then Return
        Dim Channel = CStr(CType(ComboChannel.SelectedItem, MyComboBoxItem).Tag)
        For Each Entry In VersionEntries.Where(Function(Item) Item.Channel = Channel).Take(100)
            ComboVersion.Items.Add(New MyComboBoxItem With {.Content = Entry.ToString(), .Tag = Entry})
        Next
        If ComboVersion.Items.Count > 0 Then ComboVersion.SelectedIndex = 0
        BtnInstall.IsEnabled = ComboVersion.Items.Count > 0
    End Sub

    Private Sub Install_Click(sender As Object, e As MouseButtonEventArgs)
        Dim Selected = TryCast(ComboVersion.SelectedItem, MyComboBoxItem)
        If Selected Is Nothing Then Return
        StartInstall(DirectCast(Selected.Tag, BedrockVersionEntry), Nothing)
    End Sub

    Private Sub InstallLocal_Click(sender As Object, e As MouseButtonEventArgs)
        Dim PackagePath = Dialogs.SelectFile("选择基岩版安装包", False, filter:={({"appx", "msix"}, "可解压的 Windows 应用包")}).FirstOrDefault()
        If String.IsNullOrWhiteSpace(PackagePath) Then Return
        Dim Name = Path.GetFileNameWithoutExtension(PackagePath)
        StartInstall(New BedrockVersionEntry With {.Version = Name, .Channel = If(Name.ContainsIgnoreCase("Beta"), "Preview", "Release"), .BuildType = "UWP"}, PackagePath)
    End Sub

    Private Sub StartInstall(Entry As BedrockVersionEntry, ExistingPackage As String)
        Dim ChannelFolder = If(Entry.Channel = "Preview", "preview", "release")
        Dim Root = Paths.Base & ".minecraft\bedrock\"
        Dim PackageFolder = Root & "packages\" & ChannelFolder & "\" & Entry.Version & "\"
        Dim FileName = If(String.IsNullOrWhiteSpace(ExistingPackage), $"Minecraft-{Entry.Version}-x64.appx", Path.GetFileName(ExistingPackage))
        Dim PackagePath = If(String.IsNullOrWhiteSpace(ExistingPackage), PackageFolder & FileName, ExistingPackage)
        Dim InstallFolder = Root & "versions\" & ChannelFolder & "-" & Entry.Version & "\"

        If LoaderTaskbar.Any(Function(Task) Task.Name = $"基岩版 {Entry.Version} 安装") Then
            Hint("这个基岩版版本正在安装中。", HintType.Blue)
            Return
        End If

        Dim Loaders As New List(Of LoaderBase)
        If String.IsNullOrWhiteSpace(ExistingPackage) Then
            Loaders.Add(New LoaderTask(Of String, List(Of NetFile))("解析微软下载地址",
                Sub(Task)
                    Dim DownloadUrl = If(Entry.Metadata.StartsWithF("http://", True) OrElse Entry.Metadata.StartsWithF("https://", True),
                                         Entry.Metadata, ResolveMicrosoftUpdateUrl(Entry.Metadata))
                    If String.IsNullOrWhiteSpace(DownloadUrl) Then Throw New Exception("微软更新服务没有返回可用的安装包地址。")
                    Task.Output = New List(Of NetFile) From {
                        New NetFile({DownloadUrl}, PackagePath, New FileChecker With {.MinSize = 1024 * 1024, .Hash = If(String.IsNullOrWhiteSpace(Entry.Md5), Nothing, Entry.Md5)}, True)
                    }
                End Sub) With {.ProgressWeight = 1})
            Loaders.Add(New LoaderDownload("下载基岩版安装包", New List(Of NetFile)) With {.ProgressWeight = 20})
        End If
        Loaders.Add(New LoaderTask(Of Integer, Integer)("解压基岩版文件",
            Sub(Task)
                ExtractPackage(PackagePath, InstallFolder, Task)
                Task.Output = 0
            End Sub) With {.ProgressWeight = 8})
        Loaders.Add(New LoaderTask(Of Integer, Integer)("静默注册 Windows 应用包",
            Sub(Task)
                RegisterPackage(InstallFolder, Task)
                Task.Output = 0
            End Sub) With {.ProgressWeight = 2})

        Dim Loader As New LoaderCombo(Of String)($"基岩版 {Entry.Version} 安装", Loaders) With {
            .OnStateChanged =
            Sub(Current)
                Select Case Current.State
                    Case LoadState.Loading
                        RunInUi(Sub() LabStatus.Text = $"正在安装基岩版 {Entry.Version}，可在任务管理中查看进度。")
                    Case LoadState.Finished
                        Hint($"基岩版 {Entry.Version} 安装完成。", HintType.Green)
                        RunInUi(Sub() LabStatus.Text = $"基岩版 {Entry.Version} 已安装，可以直接启动。")
                    Case LoadState.Failed
                        Hint("基岩版安装失败，详情已保留在任务管理中。", HintType.Red)
                End Select
            End Sub
        }
        LoaderTaskbarAdd(Loader)
        Loader.Start()
        FrmMain.BtnExtraDownload.ShowRefresh()
    End Sub

    Private Shared Function ResolveMicrosoftUpdateUrl(UpdateId As String) As String
        Const Endpoint As String = "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/secured"
        Const DeviceAttributes As String = "E:BranchReadinessLevel=CBB&DchuNvidiaGrfxExists=1&ProcessorIdentifier=Intel64%20Family%206%20Model%2063%20Stepping%202&CurrentBranch=rs4_release&DataVer_RS5=1942&FlightRing=Retail&AttrDataVer=57&InstallLanguage=en-US&DchuAmdGrfxExists=1&OSUILocale=en-US&InstallationType=Client&FlightingBranchName=&Version_RS5=10&UpgEx_RS5=Green&GStatus_RS5=2&OSSkuId=48&App=WU&InstallDate=1529700913&ProcessorManufacturer=GenuineIntel&AppVer=10.0.17134.471&OSArchitecture=AMD64&UpdateManagementGroup=2&IsDeviceRetailDemo=0&IsFlightingEnabled=0&TelemetryLevel=1&DefaultUserRegion=244&WuClientVer=10.0.17134.471&OSVersion=10.0.17134.472&DeviceFamily=Windows.Desktop"
        Const Template As String = "<s:Envelope xmlns:a=""http://www.w3.org/2005/08/addressing"" xmlns:s=""http://www.w3.org/2003/05/soap-envelope""><s:Header><a:Action s:mustUnderstand=""1"">http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService/GetExtendedUpdateInfo2</a:Action><a:MessageID></a:MessageID><a:To s:mustUnderstand=""1"">https://fe3.delivery.mp.microsoft.com/ClientWebService/Client.asmx/secured</a:To><o:Security s:mustUnderstand=""1"" xmlns:o=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd""><Timestamp xmlns=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd""><Created></Created><Expires></Expires></Timestamp><wuws:WindowsUpdateTicketsToken wsu:id=""ClientMSA"" xmlns:wsu=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"" xmlns:wuws=""http://schemas.microsoft.com/msus/2014/10/WindowsUpdateAuthorization""><TicketType Name=""AAD"" Version=""1.0"" Policy=""MBI_SSL""></TicketType></wuws:WindowsUpdateTicketsToken></o:Security></s:Header><s:Body><GetExtendedUpdateInfo2 xmlns=""http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService""><updateIDs><UpdateIdentity><UpdateID></UpdateID><RevisionNumber>1</RevisionNumber></UpdateIdentity></updateIDs><infoTypes><XmlUpdateFragmentType>FileUrl</XmlUpdateFragmentType></infoTypes><deviceAttributes></deviceAttributes></GetExtendedUpdateInfo2></s:Body></s:Envelope>"
        Dim Document As New Xml.XmlDocument()
        Document.LoadXml(Template)
        Dim Now = Date.UtcNow
        Document.GetElementsByTagName("MessageID")(0).InnerText = "urn:uuid:" & Guid.NewGuid().ToString()
        Document.GetElementsByTagName("UpdateID")(0).InnerText = UpdateId
        Document.GetElementsByTagName("Created")(0).InnerText = Now.ToString("o")
        Document.GetElementsByTagName("Expires")(0).InnerText = Now.AddMinutes(5).ToString("o")
        Document.GetElementsByTagName("deviceAttributes")(0).InnerText = DeviceAttributes
        Using Client As New Net.Http.HttpClient(), Content As New Net.Http.StringContent(Document.InnerXml, Encoding.UTF8, "application/soap+xml")
            Client.Timeout = TimeSpan.FromSeconds(30)
            Dim Response = Client.PostAsync(Endpoint, Content).GetAwaiter().GetResult()
            Response.EnsureSuccessStatusCode()
            Dim ResponseXml = XDocument.Parse(Response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
            Dim NamespaceValue As XNamespace = "http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService"
            Dim Urls = ResponseXml.Descendants(NamespaceValue + "Url").Select(Function(Node) Net.WebUtility.HtmlDecode(Node.Value)).Where(Function(Url) Not String.IsNullOrWhiteSpace(Url)).ToList()
            Dim Preferred = Urls.FirstOrDefault(Function(Url) Url.Contains("?P1=") OrElse Url.Contains("tlu.dl.") OrElse Url.Contains("&P2=") OrElse Url.Length > 150)
            Return If(Preferred, Urls.LastOrDefault())
        End Using
    End Function

    Private Shared Sub ExtractPackage(PackagePath As String, InstallFolder As String, Task As LoaderTask(Of Integer, Integer))
        If Not File.Exists(PackagePath) Then Throw New FileNotFoundException("找不到基岩版安装包。", PackagePath)
        DirectoryUtils.Delete(InstallFolder)
        DirectoryUtils.Create(InstallFolder)
        Using PackageStream = File.OpenRead(PackagePath), Archive As New ZipArchive(PackageStream, ZipArchiveMode.Read)
            Dim TotalBytes = Math.Max(1L, Archive.Entries.Sum(Function(Entry) Math.Max(0L, Entry.Length)))
            Dim FinishedBytes As Long = 0
            Dim Buffer(1024 * 128 - 1) As Byte
            For Each Entry In Archive.Entries
                If Task.IsCanceled Then Throw New OperationCanceledException()
                Dim Target = Path.GetFullPath(Path.Combine(InstallFolder, Entry.FullName.Replace("/", "\")))
                If Not Target.StartsWithF(Path.GetFullPath(InstallFolder), True) Then Throw New InvalidDataException("安装包包含不安全的文件路径。")
                If Entry.FullName.EndsWithF("/") OrElse Entry.FullName.EndsWithF("\") Then
                    DirectoryUtils.Create(Target)
                    Continue For
                End If
                DirectoryUtils.Create(Path.GetDirectoryName(Target))
                Using Input = Entry.Open(), Output = New FileStream(Target, FileMode.Create, FileAccess.Write, FileShare.None)
                    While True
                        Dim Count = Input.Read(Buffer, 0, Buffer.Length)
                        If Count <= 0 Then Exit While
                        Output.Write(Buffer, 0, Count)
                        FinishedBytes += Count
                        Task.Progress = Math.Min(0.99, FinishedBytes / CDbl(TotalBytes))
                        If Task.IsCanceled Then Throw New OperationCanceledException()
                    End While
                End Using
            Next
        End Using
        FileUtils.Delete(InstallFolder & "AppxSignature.p7x")
        If Not File.Exists(InstallFolder & "AppxManifest.xml") Then Throw New InvalidDataException("安装包中缺少 AppxManifest.xml，无法注册。")
        Task.Progress = 1
    End Sub

    Private Shared Sub RegisterPackage(InstallFolder As String, Task As LoaderTask(Of Integer, Integer))
        Dim Manifest = InstallFolder & "AppxManifest.xml"
        Dim EscapedManifest = Manifest.Replace("'", "''")
        Dim PowerShell = "$ErrorActionPreference='Stop'; Add-AppxPackage -Path '" & EscapedManifest & "' -Register -ForceApplicationShutdown -ForceUpdateFromAnyVersion; Write-Output 'Registered'"
        Dim Encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(PowerShell))
        Dim StartInfo As New ProcessStartInfo("powershell.exe", "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " & Encoded) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True
        }
        Task.Progress = 0.1
        Using Process = System.Diagnostics.Process.Start(StartInfo)
            Dim Output = Process.StandardOutput.ReadToEnd()
            Dim ErrorOutput = Process.StandardError.ReadToEnd()
            Process.WaitForExit()
            If Process.ExitCode <> 0 Then
                Throw New Exception("Windows 应用包注册失败。请启用 Windows 开发者模式，并确认 Microsoft.VCLibs.140 运行库已经安装。" & If(String.IsNullOrWhiteSpace(ErrorOutput), "", vbCrLf & ErrorOutput.Trim()))
            End If
            Logger.Info("基岩版应用包注册完成：" & Output.Trim())
        End Using
        Task.Progress = 1
    End Sub

    Private Sub Launch_Click(sender As Object, e As MouseButtonEventArgs)
        Try
            Process.Start(New ProcessStartInfo("minecraft://") With {.UseShellExecute = True})
        Catch ex As Exception
            Logger.Error(ex, "启动基岩版失败")
            MyMsgBox("未找到已注册的 Minecraft 基岩版，请先完成安装。", "启动基岩版", "确定")
        End Try
    End Sub

    Private Sub OpenFolder_Click(sender As Object, e As MouseButtonEventArgs)
        Dim Folder = Paths.Base & ".minecraft\bedrock\"
        DirectoryUtils.Create(Folder)
        OpenExplorer(Folder)
    End Sub

End Class
