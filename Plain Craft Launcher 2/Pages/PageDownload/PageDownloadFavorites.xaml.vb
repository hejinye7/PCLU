Public Class PageDownloadFavorites
    Private Sub PageDownloadFavorites_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        RefreshFavorites()
    End Sub

    Private Sub BtnRefreshFavorites_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnRefreshFavorites.Click
        RefreshFavorites()
    End Sub

    Public Sub RefreshFavorites()
        Try
            PanFavorites.Children.Clear()
            Dim Items = ResourceFavorites.GetAll()
            LabFavoriteCount.Text = $"已收藏 {Items.Count} 项"
            HintEmptyFavorites.Visibility = If(Items.Any(), Visibility.Collapsed, Visibility.Visible)
            CardFavorites.Visibility = If(Items.Any(), Visibility.Visible, Visibility.Collapsed)
            For Each Project In Items
                PanFavorites.Children.Add(Project.ToResourceItem(True, True))
            Next
        Catch ex As Exception
            Logger.Error(ex, "刷新社区资源收藏夹失败")
        End Try
    End Sub
End Class
