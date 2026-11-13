Imports System.Net
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>
''' 使用用户填写的 WebDAV 地址同步收藏夹和可迁移的启动器设置。
''' </summary>
Public Module PcluSync
    Private Const SyncFileName As String = "PCLU-sync.json"
    Private ReadOnly SyncKeys As String() = {
        "SystemSystemActivity", "SystemSystemTelemetry", "ToolHelpChinese",
        "ToolDownloadThread", "ToolDownloadSpeed", "ToolDownloadSource", "ToolDownloadVersion",
        "ToolDownloadTranslateV2", "ToolDownloadMod", "ToolModLocalNameStyle",
        "LaunchArgumentTitle", "LaunchArgumentInfo", "LaunchArgumentVisible", "LaunchArgumentPriority",
        "LaunchArgumentRam", "LaunchAdvanceGraphicCard", "LaunchAdvanceAiPerformance",
        "LaunchAdvanceGC", "LaunchRamType", "LaunchRamCustom",
        "LinkLatencyMode", "LinkCustomPeer", "UiLauncherHue", "UiLauncherSat", "UiLauncherDelta",
        "UiLauncherLight", "UiLauncherTheme", "UiLauncherLogo", "UiBackgroundColorful",
        "UiBackgroundOpacity", "UiBackgroundBlur", "UiBackgroundSuit", "UiLogoType", "UiLogoText"
    }

    Public Sub Upload()
        Dim Url = GetSyncUrl()
        Dim Headers = GetHeaders()
        Dim SettingsJson As New JObject
        For Each Key In SyncKeys
            SettingsJson(Key) = JToken.FromObject(Settings.Get(Key))
        Next
        Dim Favorites = Settings.Get(Of String)("DownloadFavorites")
        Dim Payload As New JObject(
            New JProperty("format", 1),
            New JProperty("updatedAt", DateTime.UtcNow.ToString("o")),
            New JProperty("settings", SettingsJson),
            New JProperty("favorites", If(String.IsNullOrWhiteSpace(Favorites), New JArray, JArray.Parse(Favorites)))
        )
        NetRequestByClient(Url, New HttpMethod("PUT"), Payload.ToString(Newtonsoft.Json.Formatting.None),
                           ContentType:="application/json; charset=utf-8", Headers:=Headers, Timeout:=30000)
    End Sub

    Public Sub Download()
        Dim JsonText = NetRequestByClient(GetSyncUrl(), Headers:=GetHeaders(), Timeout:=30000, RequireJson:=True)
        Dim Payload = JsonText.DeserializeJson(Of JObject)()
        If Payload("format")?.Value(Of Integer)() <> 1 Then Throw New FormatException("PCLU 同步文件格式不受支持。")
        Dim Favorites = TryCast(Payload("favorites"), JArray)
        If Favorites IsNot Nothing Then Settings.SetSafe("DownloadFavorites", Favorites.ToString(Newtonsoft.Json.Formatting.None))
        Dim SettingsJson = TryCast(Payload("settings"), JObject)
        If SettingsJson IsNot Nothing Then
            For Each Key In SyncKeys
                Dim Token = SettingsJson(Key)
                If Token Is Nothing Then Continue For
                Dim Entry As Settings.Setting = Nothing
                If Settings.Entries.TryGetValue(Key, Entry) AndAlso Not Entry.Encrypted Then
                    Settings.Set(Key, Token.ToObject(Entry.Type))
                End If
            Next
        End If
    End Sub

    Private Function GetSyncUrl() As String
        Dim BaseUrl = Settings.Get(Of String)("PcluSyncUrl").Trim()
        If String.IsNullOrWhiteSpace(BaseUrl) Then Throw New ArgumentException("请先填写 WebDAV 地址。")
        Dim Parsed As Uri = Nothing
        If Not Uri.TryCreate(BaseUrl, UriKind.Absolute, Parsed) OrElse (Parsed.Scheme <> Uri.UriSchemeHttp AndAlso Parsed.Scheme <> Uri.UriSchemeHttps) Then
            Throw New ArgumentException("WebDAV 地址必须是 http 或 https 地址。")
        End If
        Return BaseUrl.TrimEnd("/"c) & "/" & SyncFileName
    End Function

    Private Function GetHeaders() As String(,)
        Dim User = Settings.Get(Of String)("PcluSyncUser")
        Dim Password = Settings.Get(Of String)("PcluSyncPassword")
        If String.IsNullOrWhiteSpace(User) Then Throw New ArgumentException("请先填写 WebDAV 用户名。")
        Dim Token = Convert.ToBase64String(Encoding.UTF8.GetBytes(User & ":" & Password))
        Return {{"Authorization", "Basic " & Token}}
    End Function
End Module
