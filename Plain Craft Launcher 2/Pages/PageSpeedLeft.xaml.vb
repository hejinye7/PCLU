Public Class PageSpeedLeft
    Private Const WatcherInterval As Integer = 300

    '初始化
    Private IsLoad As Boolean = False
    Private Sub Page_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '进入时就刷新一次显示
        Watcher()

        '如果在页面切换动画的 “上一页消失” 部分已经完成了下载，就直接尝试返回
        TryReturnToHome()

        If IsLoad Then Return
        IsLoad = True

        '监控定时器
        Dim timer As New Threading.DispatcherTimer With {.Interval = New TimeSpan(0, 0, 0, 0, WatcherInterval)}
        AddHandler timer.Tick, AddressOf Watcher
        timer.Start()

        '非调试模式隐藏线程数
        If Not ModeDebug Then
            RowDefinitions(12).Height = New GridLength(0)
            RowDefinitions(13).Height = New GridLength(0)
            RowDefinitions(14).Height = New GridLength(0)
            RowDefinitions(15).Height = New GridLength(0)
        End If

    End Sub

    '定时器任务
    Private ReadOnly RightCards As New Dictionary(Of Integer, MyCard)
    Private Sub Watcher()
        If Not FrmMain.PageCurrent = FormMain.PageType.TaskManager Then Return
        Try

#Region "更新左边栏"
            If Not LoaderTaskbar.Any() Then
                '无任务
                LabProgress.Text = "100 %"
                LabSpeed.Text = "0 B/s"
                LabFile.Text = "0"
                LabThread.Text = "0 / " & NetTaskThreadLimit
            Else
                '有任务，输出基本信息
                Dim Tasks = LoaderTaskbar.Where(Function(l) l.Show).ToList() '筛选掉启动 MC 的任务（#6270）
                Dim RawPercent As Double = If(Tasks.Any, Tasks.Select(Function(l) l.Progress).Average().Clamp(0, 1), 1)
                Dim PredictText As String = Math.Floor(RawPercent * 100) & "." & Math.Floor((RawPercent * 100 - Math.Floor(RawPercent * 100)) * 100).ToString.EnsureLength("0", 2) & " %"
                LabProgress.Text = If(RawPercent > 0.999999, "100 %", PredictText)
                LabSpeed.Text = StringUtils.FormatByteSize(NetManager.Speed) & "/s"
                LabFile.Text = If(NetManager.FileRemain < 0, "0*", NetManager.FileRemain)
                LabThread.Text = NetTaskThreadCount & " / " & NetTaskThreadLimit
            End If
#End Region

        Catch ex As Exception
            Logger.Error(ex, "任务管理左栏监视出错")
        End Try
        If FrmSpeedRight Is Nothing OrElse FrmSpeedRight.PanMain Is Nothing Then Return
        Try
            For Each Loader In LoaderTaskbar.ToList
                TaskRefresh(Loader)
            Next
        Catch ex As Exception
            Logger.Error(ex, "任务管理右栏监视出错")
        End Try
    End Sub
    Public Sub TaskRefresh(Loader As LoaderBase)
        If Loader Is Nothing OrElse Not Loader.Show Then Return
        Try
            '获取实际加载器列表
            Dim LoaderList As List(Of LoaderBase) = CType(Loader, Object).GetLoaderList()
            If RightCards.ContainsKey(Loader.Uuid) Then
                '已有此卡片
                Dim Card As MyCard = RightCards(Loader.Uuid)
                Dim NewValue As Double = Loader.Progress + Loader.State
                If Val(Card.Tag) = NewValue Then Return
                Card.Tag = NewValue
                Dim ContentGrid = Card.Children.OfType(Of Grid).FirstOrDefault(Function(Element) Equals(Element.Tag, "TaskContent"))
                If ContentGrid Is Nothing Then
                    Logger.Warn($"找不到任务卡片内容：{Loader.Name}")
                    Return
                End If
                Try
                    Select Case Loader.State
                        Case LoadState.Failed
#Region "失败，更新卡片"
                            ContentGrid.RowDefinitions.Clear()
                            ContentGrid.Children.Clear()
                            ContentGrid.Children.Add(GetObjectFromXML("<Path xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" Stretch=""Uniform"" Tag=""Failed"" Data=""F1 M2.5,0 L0,2.5 7.5,10 0,17.5 2.5,20 10,12.5 17.5,20 20,17.5 12.5,10 20,2.5 17.5,0 10,7.5 2.5,0Z"" Height=""15"" Width=""15"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""0"" Fill=""{DynamicResource ColorBrush3}"" Margin=""0,1,0,0"" VerticalAlignment=""Top""/>"))
                            Dim Tb As TextBlock = GetObjectFromXML("<TextBlock xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" TextWrapping=""Wrap"" HorizontalAlignment=""Left"" ToolTip=""单击复制错误详情"" Grid.Column=""1"" Grid.Row=""0"" Margin=""0,0,0,5"" />")
                            Tb.Text = Loader.Error.GetDisplay(True)
                            AddHandler Tb.MouseLeftButtonDown,
                            Sub(sender As TextBlock, e As EventArgs)
                                ClipboardSet(sender.Text, False)
                                Hint("已复制错误详情！", HintType.Green)
                            End Sub
                            ContentGrid.Children.Add(Tb)
#End Region
                        Case LoadState.Finished, LoadState.Canceled
#Region "完成或中断，销毁卡片并返回"
                            AniDispose(Card, True, AddressOf TryReturnToHome)
#End Region
                        Case LoadState.Loading, LoadState.Waiting
#Region "进度不同，更新卡片"
                            Try
                                If ContentGrid.Children.Count < LoaderList.Count * 2 Then
                                    Logger.Warn($"刷新任务管理卡片 {Loader.Name} 失败：卡片中仅有 {ContentGrid.Children.Count} 个子项，要求至少有 {LoaderList.Count * 2} 个子项")
                                    Exit Try
                                End If
                                Dim Row As Integer = 0
                                For Each SubTask In LoaderList
                                    Select Case SubTask.State
                                        Case LoadState.Waiting
                                            If CType(ContentGrid.Children(Row * 2), FrameworkElement).Tag <> "Waiting" Then
                                                ContentGrid.Children.RemoveAt(Row * 2)
                                                ContentGrid.Children.Insert(Row * 2, GetObjectFromXML("<Path xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2"" Stretch=""Uniform"" Tag=""Waiting"" Data=""F1 M5,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 Z"" Width=""18"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""" & Row & """ Fill=""{DynamicResource ColorBrush3}"" Margin=""0,7,0,0"" VerticalAlignment=""Top"" Height=""6""/>"))
                                            End If
                                        Case LoadState.Loading
                                            If CType(ContentGrid.Children(Row * 2), FrameworkElement).Tag <> "Loading" Then
                                                ContentGrid.Children.RemoveAt(Row * 2)
                                                ContentGrid.Children.Insert(Row * 2, GetObjectFromXML("<TextBlock xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2"" Text=""" & Math.Floor(SubTask.Progress * 100) & "%"" Tag=""Loading"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""" & Row & """ Foreground=""{DynamicResource ColorBrush3}""/>"))
                                            Else
                                                CType(ContentGrid.Children(Row * 2), TextBlock).Text = Math.Floor(SubTask.Progress * 100) & "%"
                                            End If
                                        Case LoadState.Finished
                                            If CType(ContentGrid.Children(Row * 2), FrameworkElement).Tag <> "Finished" Then
                                                ContentGrid.Children.RemoveAt(Row * 2)
                                                ContentGrid.Children.Insert(Row * 2, GetObjectFromXML("<Path xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2"" Stretch=""Uniform"" Tag=""Finished"" Data=""F1 M 23.7501,33.25L 34.8334,44.3333L 52.2499,22.1668L 56.9999,26.9168L 34.8334,53.8333L 19.0001,38L 23.7501,33.25 Z"" Height=""16"" Width=""15"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""" & Row & """ Fill=""{DynamicResource ColorBrush3}"" Margin=""0,3,0,0"" VerticalAlignment=""Top""/>"))
                                            End If
                                    End Select
                                    Row += 1
                                Next
                            Catch ex As Exception
                                Logger.Error(ex, $"刷新任务管理卡片 {Loader.Name} 失败")
                            End Try
#End Region
                    End Select
                Catch ex As Exception
                    Logger.Error(ex, $"更新任务管理显示失败（{Loader.State}）")
                End Try
            ElseIf Not (Loader.State = LoadState.Canceled OrElse Loader.State = LoadState.Finished) Then
                Try
#Region "没有卡片且未中断或完成，添加新的卡片"
                    Dim CardXAML As String = "
                        <local:MyCard xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2""
                            Tag=""" & (Loader.Progress + Loader.State) & """ Title=""" & StringUtils.XmlEscape(Loader.Name) & """ Margin=""0,0,0,15"">
                            <Grid Tag=""TaskContent"" Margin=""14,40,15,10"">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width=""50""/>
                                    <ColumnDefinition/>
                                </Grid.ColumnDefinitions>
                                <Grid.RowDefinitions>"
                    For Each SubTask In LoaderList
                        CardXAML += "<RowDefinition Height=""26""/>"
                    Next
                    CardXAML += "</Grid.RowDefinitions>"
                    Dim Row As Integer = 0
                    For Each SubTask In LoaderList
                        Select Case SubTask.State
                            Case LoadState.Waiting
                                CardXAML += "<Path Stretch=""Uniform"" Tag=""Waiting"" Data=""F1 M5,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 m15,0 a5,5 360 1 0 0,0.0001 Z"" Width=""18"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""" & Row & """ Fill=""{DynamicResource ColorBrush3}"" Margin=""0,7,0,0"" VerticalAlignment=""Top"" Height=""6""/>"
                            Case LoadState.Loading
                                CardXAML += "<TextBlock Text=""" & Math.Floor(SubTask.Progress * 100) & "%"" Tag=""Loading"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""" & Row & """ Foreground=""{DynamicResource ColorBrush3}"" />"
                            Case LoadState.Finished
                                CardXAML += "<Path Stretch=""Uniform"" Tag=""Finished"" Data=""F1 M 23.7501,33.25L 34.8334,44.3333L 52.2499,22.1668L 56.9999,26.9168L 34.8334,53.8333L 19.0001,38L 23.7501,33.25 Z"" Height=""16"" Width=""15"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""" & Row & """ Fill=""{DynamicResource ColorBrush3}"" Margin=""0,3,0,0"" VerticalAlignment=""Top""/>"
                            Case Else
                                CardXAML += "<Path Stretch=""Uniform"" Tag=""Failed"" Data=""F1 M2.5,0 L0,2.5 7.5,10 0,17.5 2.5,20 10,12.5 17.5,20 20,17.5 12.5,10 20,2.5 17.5,0 10,7.5 2.5,0Z"" Height=""15"" Width=""15"" HorizontalAlignment=""Center"" Grid.Column=""0"" Grid.Row=""" & Row & """ Fill=""{DynamicResource ColorBrush3}"" Margin=""0,1,0,0"" VerticalAlignment=""Top""/>"
                        End Select
                        CardXAML += "<TextBlock Text=""" & StringUtils.XmlEscape(SubTask.Name) & """ HorizontalAlignment=""Left"" Grid.Column=""1"" Grid.Row=""" & Row & """/>"
                        Row += 1
                    Next
                    CardXAML += "</Grid></local:MyCard>"
                    '实例化控件
                    Dim Card As MyCard
                    Try
                        Card = GetObjectFromXML(CardXAML)
                    Catch ex As Exception
                        Logger.Warn(ex, "新建任务管理卡片失败")
                        Logger.Info($"出错的卡片内容：{vbCrLf}{CardXAML}")
                        Throw
                    End Try
                    If ContainsDownloadLoader(Loader) Then
                        AddAiDownloadGlow(Card)
                    End If
                    FrmSpeedRight.PanMain.Children.Insert(0, Card)
                    RightCards.Add(Loader.Uuid, Card)
                    Logger.Info($"新建任务管理卡片：{Loader.Name}")
                    '添加取消按钮
                    Dim Cancel As New MyIconButton With {.Name = "BtnCancel", .Logo = "F1 M2,0 L0,2 8,10 0,18 2,20 10,12 18,20 20,18 12,10 20,2 18,0 10,8 2,0Z", .Height = 20, .Margin = New Thickness(0, 10, 10, 0), .LogoScale = 1.1, .HorizontalAlignment = HorizontalAlignment.Right, .VerticalAlignment = VerticalAlignment.Top}
                    Card.Children.Add(Cancel)
                    AddHandler Cancel.Click,
                    Sub(sender As MyIconButton, e As EventArgs)
                        AniDispose(sender, False)
                        AniDispose(Card, True, Sub() If FrmSpeedRight.PanMain.Children.Count = 0 AndAlso FrmMain.PageCurrent = FormMain.PageType.TaskManager Then FrmMain.PageBack())
                        RightCards.Remove(Loader.Uuid)
                        LoaderTaskbar.Remove(Loader)
                        Logger.Info($"关闭任务管理卡片：{Loader.Name}，且移出任务列表")
                        RunInThread(Sub() Loader.Cancel())
                    End Sub
                    '如果已经失败，再刷新一次，修改成失败的控件
                    If Loader.State = LoadState.Failed Then
                        Card.Tag = Nothing '避免重复导致刷新无效
                        TaskRefresh(Loader)
                    End If
#End Region
                Catch ex As Exception
                    Logger.Error(ex, "添加任务管理卡片失败")
                End Try
            End If
        Catch ex As Exception
            Logger.Error(ex, "刷新任务管理显示失败")
        End Try
    End Sub

    ''' <summary>
    ''' 扫描完整任务树，包括不在任务卡正文中显示的下载节点。
    ''' </summary>
    Private Shared Function ContainsDownloadLoader(Loader As LoaderBase) As Boolean
        If TypeOf Loader Is LoaderDownload Then Return True
        If Not GeneralUtils.IsGenericInstanceOf(Loader, GetType(LoaderCombo(Of ))) Then Return False
        For Each SubLoader As LoaderBase In CType(Loader, Object).Loaders
            If ContainsDownloadLoader(SubLoader) Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' 为由自适应线路算法接管的下载任务添加旋转彩色边缘。
    ''' </summary>
    Private Shared Sub AddAiDownloadGlow(Card As MyCard)
        Dim GlowLayer As New Grid With {.IsHitTestVisible = False, .Margin = New Thickness(-1)}
        Dim GlowBorder As New Border With {
            .CornerRadius = New CornerRadius(7), .BorderThickness = New Thickness(3),
            .Opacity = 0.72, .Effect = New Effects.BlurEffect With {.Radius = 8},
            .BorderBrush = CreateAiGlowBrush(7)
        }
        Dim EdgeBorder As New Border With {
            .CornerRadius = New CornerRadius(6), .BorderThickness = New Thickness(2),
            .Opacity = 0.9, .Margin = New Thickness(1),
            .BorderBrush = CreateAiGlowBrush(7)
        }
        GlowLayer.Children.Add(GlowBorder)
        GlowLayer.Children.Add(EdgeBorder)
        Card.Children.Insert(2, GlowLayer)
    End Sub

    Private Shared Function CreateAiGlowBrush(SecondsPerTurn As Double) As LinearGradientBrush
        Dim Rotation As New RotateTransform(0, 0.5, 0.5)
        Dim Brush As New LinearGradientBrush With {
            .StartPoint = New Point(0, 0.5), .EndPoint = New Point(1, 0.5),
            .RelativeTransform = Rotation
        }
        Brush.GradientStops.Add(New GradientStop(Color.FromRgb(0, 210, 255), 0))
        Brush.GradientStops.Add(New GradientStop(Color.FromRgb(91, 86, 255), 0.22))
        Brush.GradientStops.Add(New GradientStop(Color.FromRgb(255, 65, 180), 0.45))
        Brush.GradientStops.Add(New GradientStop(Color.FromRgb(255, 183, 48), 0.7))
        Brush.GradientStops.Add(New GradientStop(Color.FromRgb(53, 235, 170), 0.86))
        Brush.GradientStops.Add(New GradientStop(Color.FromRgb(0, 210, 255), 1))
        Dim Animation As New System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(SecondsPerTurn)) With {
            .RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        }
        Rotation.BeginAnimation(RotateTransform.AngleProperty, Animation)
        Return Brush
    End Function

    Public Sub TaskRemove(Loader As Object)
        If RightCards.ContainsKey(Loader.Uuid) Then
            RunInUiWait(
            Sub()
                '移除已有的卡片
                Dim Card As Grid = RightCards(Loader.Uuid)
                FrmSpeedRight.PanMain.Children.Remove(Card)
                RightCards.Remove(Loader.Uuid)
                Logger.Info($"移除任务管理卡片：{Loader.Name}")
            End Sub)
        End If
    End Sub

    ''' <summary>
    ''' 若没有任务，尝试返回主页。
    ''' </summary>
    Private Sub TryReturnToHome()
        If FrmSpeedRight.PanMain.Children.Count = 0 AndAlso FrmMain.PageCurrent = FormMain.PageType.TaskManager Then
            FrmMain.PageBack()
        End If
    End Sub

End Class
