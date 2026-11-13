Public Class PageLinkMain

    Public Enum LinkStates
        Waiting
        Loading
        Failed
        Finished
    End Enum

    Public Shared LinkState As LinkStates = LinkStates.Waiting
    Private Shared PendingCode As String = Nothing
    Private RefreshTimer As System.Windows.Threading.DispatcherTimer
    Private RefreshBusy As Boolean
    Private Working As Boolean
    Private LastRoom As String = ""
    Private LastUrl As String = ""

    Private Sub PageLinkMain_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        CardManage.IsSwapped = True
        If RefreshTimer Is Nothing Then
            RefreshTimer = New System.Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromSeconds(1)}
            AddHandler RefreshTimer.Tick, AddressOf RefreshTick
        End If
        RefreshTimer.Start()
        RefreshState()
        If PendingCode IsNot Nothing Then
            Dim Code As String = PendingCode
            PendingCode = Nothing
            JoinInternal(Code)
        End If
    End Sub

    Private Sub PageLinkMain_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        RefreshTimer?.Stop()
    End Sub

    Private Sub RefreshTick(sender As Object, e As EventArgs)
        RefreshState()
    End Sub

    Private Sub RefreshState()
        If RefreshBusy Then Return
        RefreshBusy = True
        Threading.Tasks.Task.Run(
            Sub()
                Dim Snapshot = ModTerracotta.ReadState(False)
                Dim Text As String = ModTerracotta.StatusText(Snapshot)
                RunInUi(
                    Sub()
                        RefreshBusy = False
                        LabStatus.Text = Text
                        If Snapshot.Ok Then
                            LastUrl = Snapshot.Url
                            If Snapshot.Stage.StartsWith("host-") AndAlso Snapshot.Room <> "" Then
                                LastRoom = Snapshot.Room
                                If TextRoom.Text <> Snapshot.Room Then TextRoom.Text = Snapshot.Room
                            End If
                            Select Case Snapshot.Stage
                                Case "host-scanning", "host-starting", "guest-connecting", "guest-starting"
                                    LinkState = LinkStates.Loading
                                Case "host-ok", "guest-ok"
                                    LinkState = LinkStates.Finished
                                Case "exception"
                                    LinkState = If(Snapshot.Room = "" AndAlso Snapshot.Url = "", LinkStates.Waiting, LinkStates.Failed)
                                Case Else
                                    LinkState = LinkStates.Waiting
                            End Select
                        Else
                            LinkState = LinkStates.Waiting
                        End If
                    End Sub)
            End Sub)
    End Sub

    Private Sub RunOperation(Status As String, Action As Func(Of String))
        If Working Then Return
        Working = True
        LabStatus.Text = Status
        SetButtons(False)
        Threading.Tasks.Task.Run(
            Sub()
                Dim Message As String
                Try
                    If Not ModTerracotta.IsInstalled Then
                        Message = ModTerracotta.Install()
                        If Not ModTerracotta.IsInstalled Then
                            RunInUi(Sub() FinishOperation(Message, True))
                            Return
                        End If
                    End If
                    Message = Action()
                    RunInUi(Sub() FinishOperation(Message, False))
                Catch ex As Exception
                    Logger.Error(ex, "陶瓦联机操作失败")
                    RunInUi(Sub() FinishOperation("操作失败：" & ex.Message, True))
                End Try
            End Sub)
    End Sub

    Private Sub FinishOperation(Message As String, IsError As Boolean)
        Working = False
        SetButtons(True)
        LabStatus.Text = Message
        Hint(Message, If(IsError, HintType.Red, HintType.Blue), False)
        RefreshState()
    End Sub

    Private Sub SetButtons(Enabled As Boolean)
        PanSelectCreate.IsEnabled = Enabled
        PanSelectJoin.IsEnabled = Enabled
        BtnInstall.IsEnabled = Enabled
        BtnCopy.IsEnabled = Enabled
        BtnReset.IsEnabled = Enabled
        BtnStop.IsEnabled = Enabled
    End Sub

    Private Sub PanSelectCreate_Click() Handles PanSelectCreate.MouseLeftButtonUp
        RunOperation("正在准备陶瓦并创建房间...", AddressOf ModTerracotta.StartHost)
    End Sub

    Private Sub PanSelectJoin_Click() Handles PanSelectJoin.MouseLeftButtonUp
        Join()
    End Sub

    Public Sub JoinInternal(Code As String)
        Dim ErrorText As String = ValidateCodeFormat(Code)
        If ErrorText IsNot Nothing Then
            Hint(ErrorText, HintType.Red)
            Return
        End If
        Dim Normalized As String = ModTerracotta.NormalizeCode(Code)
        TextRoom.Text = Normalized
        RunOperation("正在准备陶瓦并加入房间...", Function() ModTerracotta.JoinRoom(Normalized))
    End Sub

    Private Sub BtnInstall_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnInstall.Click
        If Working Then Return
        Working = True
        SetButtons(False)
        LabStatus.Text = "正在下载、校验并安装陶瓦..."
        Threading.Tasks.Task.Run(
            Sub()
                Dim Message As String = ModTerracotta.Install()
                RunInUi(Sub() FinishOperation(Message, Not ModTerracotta.IsInstalled))
            End Sub)
    End Sub

    Private Sub BtnReset_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnReset.Click
        Threading.Tasks.Task.Run(
            Sub()
                ModTerracotta.ResetState()
                RunInUi(
                    Sub()
                        LastRoom = ""
                        LastUrl = ""
                        TextRoom.Text = ""
                        LinkState = LinkStates.Waiting
                        RefreshState()
                    End Sub)
            End Sub)
    End Sub

    Private Sub BtnStop_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnStop.Click
        Threading.Tasks.Task.Run(
            Sub()
                ModTerracotta.Shutdown()
                RunInUi(
                    Sub()
                        LastRoom = ""
                        LastUrl = ""
                        TextRoom.Text = ""
                        LinkState = LinkStates.Waiting
                        LabStatus.Text = "陶瓦已关闭。"
                    End Sub)
            End Sub)
    End Sub

    Private Sub BtnCopy_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnCopy.Click
        Dim Value As String = If(LastRoom <> "", LastRoom, LastUrl)
        If Value = "" Then
            Hint("当前没有可复制的房间码或直连地址。", HintType.Red)
            Return
        End If
        ClipboardSet(Value)
    End Sub

    Public Function TryExit(Silent As Boolean, Closing As Boolean) As Boolean
        If Not Closing OrElse ModTerracotta.RunningPort() <= 0 Then Return False
        If Not Silent AndAlso MyMsgBox("陶瓦联机仍在运行，关闭启动器会同时断开联机。是否继续？", "关闭联机", "继续关闭", "取消") = 2 Then Return True
        ModTerracotta.Shutdown()
        Return False
    End Function

    Public Shared Sub Join()
        Dim Code As String = MyMsgBoxInput("输入陶瓦房间码", "输入房主发给你的 U/XXXX-XXXX-XXXX-XXXX 房间码。")
        If Code IsNot Nothing Then Join(Code)
    End Sub

    Public Shared Sub Join(Code As String)
        Dim ErrorText As String = ValidateCodeFormat(Code)
        If ErrorText IsNot Nothing Then
            Hint(ErrorText, HintType.Red)
            Return
        End If
        If FrmMain.PageCurrent = FormMain.PageType.Link AndAlso FrmLinkMain IsNot Nothing Then
            FrmLinkMain.JoinInternal(Code)
        Else
            PendingCode = Code
            FrmMain.PageChange(FormMain.PageType.Link, FormMain.PageSubType.LinkMain)
        End If
    End Sub

    Public Shared Function ValidateCodeFormat(Code As String) As String
        If String.IsNullOrWhiteSpace(Code) Then Return "房间码不能为空。"
        Dim Normalized As String = ModTerracotta.NormalizeCode(Code)
        If Normalized = "" Then Return "房间码格式有误，应为 U/XXXX-XXXX-XXXX-XXXX。"
        If Not ModTerracotta.CheckCode(Normalized) Then Return "房间码校验失败，请检查是否抄错。"
        Return Nothing
    End Function

End Class
