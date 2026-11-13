Imports Newtonsoft.Json.Linq

Public Class PageDownloadCommunityClient
    Private IsLoadingVersions As Boolean
    Private LabyManifest As JObject

    Private Async Sub PageDownloadCommunityClient_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If ComboLabyChannel.Items.Count > 0 Then Return
        IsLoadingVersions = True
        ComboLabyChannel.Items.Add(New MyComboBoxItem With {.Content = "正式版", .Tag = "production"})
        ComboLabyChannel.Items.Add(New MyComboBoxItem With {.Content = "预览版", .Tag = "snapshot"})
        ComboLabyChannel.SelectedIndex = 0
        IsLoadingVersions = False
        Await LoadLabyVersionsAsync()
    End Sub

    Private Async Sub ComboLabyChannel_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboLabyChannel.SelectionChanged
        If IsLoadingVersions OrElse Not IsLoaded Then Return
        Await LoadLabyVersionsAsync()
    End Sub

    Private Async Function LoadLabyVersionsAsync() As Task
        If ComboLabyChannel.SelectedItem Is Nothing Then Return
        IsLoadingVersions = True
        ComboLabyMinecraft.Items.Clear()
        BtnInstallLaby.IsEnabled = False
        TextLabyStatus.Text = "正在读取 LabyMod 官方版本清单……"
        Try
            Dim Channel = CStr(CType(ComboLabyChannel.SelectedItem, MyComboBoxItem).Tag)
            Dim Manifest As JObject = Nothing
            Await Task.Run(Sub()
                               Manifest = NetRequestByClientRetry(
                                   $"https://releases.r2.labymod.net/api/v1/manifest/{Channel}/latest.json", RequireJson:=True).DeserializeJson(Of JObject)()
                           End Sub)
            LabyManifest = Manifest
            For Each Entry As JObject In CType(Manifest("minecraftVersions"), JArray).Children(Of JObject)()
                Dim Version = Entry("version")?.ToString()
                If Not String.IsNullOrWhiteSpace(Version) Then ComboLabyMinecraft.Items.Add(New MyComboBoxItem With {.Content = Version, .Tag = Entry.DeepClone()})
            Next
            If ComboLabyMinecraft.Items.Count = 0 Then Throw New Exception("官方清单中没有可安装的 Minecraft 版本。")
            ComboLabyMinecraft.SelectedIndex = 0
            BtnInstallLaby.IsEnabled = True
            TextLabyStatus.Text = $"LabyMod {Manifest("labyModVersion")} · {ComboLabyMinecraft.Items.Count} 个游戏版本"
        Catch ex As Exception
            TextLabyStatus.Text = "无法读取 LabyMod 官方清单：" & ex.GetDisplay(False)
            Logger.Error(ex, "获取 LabyMod 版本清单失败")
        Finally
            IsLoadingVersions = False
        End Try
    End Function

    Public Async Sub RefreshPage()
        If IsLoaded Then Await LoadLabyVersionsAsync()
    End Sub

    Private Sub InstallLaby_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnInstallLaby.Click
        If ComboLabyMinecraft.SelectedItem Is Nothing OrElse ComboLabyChannel.SelectedItem Is Nothing OrElse LabyManifest Is Nothing Then Return
        Try
            Dim Entry = CType(CType(ComboLabyMinecraft.SelectedItem, MyComboBoxItem).Tag, JObject)
            Dim MinecraftVersion = Entry("version").ToString()
            Dim Channel = CStr(CType(ComboLabyChannel.SelectedItem, MyComboBoxItem).Tag)
            Dim InstanceName = "LabyMod-" & MinecraftVersion
            Dim Request As New McInstallRequest With {
                .NewInstanceName = InstanceName,
                .InstanceFolder = $"{McFolderSelected}versions\{InstanceName}\",
                .Components = New McComponents(MinecraftVersion, New Dictionary(Of ModLoaders, String)),
                .MinecraftJson = DlClientListGet(MinecraftVersion),
                .LabyModChannel = Channel,
                .LabyModCommitReference = LabyManifest("commitReference").ToString()
            }
            If McInstallStart(Request) Then TextLabyStatus.Text = $"已开始安装 LabyMod {LabyManifest("labyModVersion")}（Minecraft {MinecraftVersion}）。"
        Catch ex As Exception
            Logger.Error(ex, "开始安装 LabyMod 失败")
            MyMsgBox("无法开始安装 LabyMod：" & ex.GetDisplay(False), "LabyMod 安装", "确定")
        End Try
    End Sub
End Class
