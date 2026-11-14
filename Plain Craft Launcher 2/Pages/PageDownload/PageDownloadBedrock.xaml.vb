Public Class PageDownloadBedrock
    Private Sub OpenBedrockStore_Click(sender As Object, e As MouseButtonEventArgs)
        Try
            Process.Start(New ProcessStartInfo("ms-windows-store://pdp/?productid=9NBLGGH2JHXJ") With {.UseShellExecute = True})
        Catch ex As Exception
            Logger.Warn(ex, "无法打开基岩版 Microsoft Store 页面，改用官方网页")
            StartProcess(New ProcessStartInfo("https://apps.microsoft.com/detail/9NBLGGH2JHXJ"))
        End Try
    End Sub
End Class
