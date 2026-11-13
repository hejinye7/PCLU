Imports Newtonsoft.Json.Linq

''' <summary>
''' 社区资源收藏夹。收藏数据随 PCLU 便携设置保存在 PCL\Settings.ini。
''' </summary>
Public Module ResourceFavorites
    Private Const SettingKey As String = "DownloadFavorites"
    Private ReadOnly SyncLockObject As New Object

    Public Function GetAll() As List(Of ResourceProject)
        SyncLock SyncLockObject
            Try
                Dim Raw = Settings.Get(Of String)(SettingKey)
                If String.IsNullOrWhiteSpace(Raw) Then Return New List(Of ResourceProject)
                Return Raw.DeserializeJson(Of JArray)().Children(Of JObject)().Select(Function(Item) New ResourceProject(Item)).ToList()
            Catch ex As Exception
                Logger.Warn(ex, "读取社区资源收藏夹失败")
                Return New List(Of ResourceProject)
            End Try
        End SyncLock
    End Function

    Public Function Contains(Project As ResourceProject) As Boolean
        If Project Is Nothing Then Return False
        Return GetAll().Any(Function(Item) Item.Platform = Project.Platform AndAlso Item.Id = Project.Id)
    End Function

    Public Sub Toggle(Project As ResourceProject)
        If Project Is Nothing Then Return
        SyncLock SyncLockObject
            Dim Items = GetAll()
            Dim Existing = Items.FirstOrDefault(Function(Item) Item.Platform = Project.Platform AndAlso Item.Id = Project.Id)
            If Existing Is Nothing Then
                Items.Insert(0, Project)
            Else
                Items.Remove(Existing)
            End If
            Settings.Set(SettingKey, New JArray(Items.Select(Function(Item) Item.ToJson())).ToString(Newtonsoft.Json.Formatting.None))
        End SyncLock
    End Sub
End Module
