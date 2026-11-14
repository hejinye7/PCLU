Public Class PageLoginAuth
    Private IsFirstLoad As Boolean = True
    Private IsReloading As Boolean = False
    ''' <summary>
    ''' 刷新页面显示的所有信息。
    ''' </summary>
    Public Sub Reload(KeepInput As Boolean)
        Dim LoginType = If(McInstanceSelected Is Nothing, Settings.Get(Of Integer)("LoginPageType"), Settings.Get(Of Integer)("VersionServerLogin", Instance:=McInstanceSelected))
        Dim ServerBase = If(McInstanceSelected Is Nothing, Settings.Get(Of String)("CacheAuthServerServer"), Settings.Get(Of String)("VersionServerAuthServer", Instance:=McInstanceSelected))
        If LoginType = 5 AndAlso String.IsNullOrWhiteSpace(ServerBase) Then ServerBase = "https://littleskin.cn/api/yggdrasil"
        IsReloading = True
        TextAuthServer.Text = ServerBase
        '记住密码
        CheckRemember.Checked = Settings.Get(Of Boolean)("LoginRemember")
        If KeepInput AndAlso Not IsFirstLoad Then '避免第一次就以 KeepInput 的方式加载，导致文本框里没东西
            '保留输入，只刷新下拉框列表
            Dim Input As String = ComboName.Text
            ComboName.ItemsSource = If(Settings.Get(Of String)("LoginAuthEmail") = "", Nothing, Settings.Get(Of String)("LoginAuthEmail").Split("¨"))
            ComboName.Text = Input
        Else
            '不保留输入，刷新列表后自动选择第一项
            If Settings.Get(Of String)("LoginAuthEmail") = "" Then
                ComboName.ItemsSource = Nothing
            Else
                ComboName.ItemsSource = Settings.Get(Of String)("LoginAuthEmail").Split("¨")
                ComboName.Text = Settings.Get(Of String)("LoginAuthEmail").BeforeFirst("¨")
                If Settings.Get(Of Boolean)("LoginRemember") Then TextPass.Password = Settings.Get(Of String)("LoginAuthPass").BeforeFirst("¨").Trim
            End If
        End If
        IsFirstLoad = False
        IsReloading = False
    End Sub
    ''' <summary>
    ''' 获取当前页面的登录信息。
    ''' </summary>
    Public Shared Function GetLoginData() As McLoginServer
        Dim ServerBase As String = If(FrmLoginAuth Is Nothing, If(IsNothing(McInstanceSelected), Settings.Get(Of String)("CacheAuthServerServer"), Settings.Get(Of String)("VersionServerAuthServer", Instance:=McInstanceSelected)), FrmLoginAuth.TextAuthServer.Text)
        Dim LoginType = If(McInstanceSelected Is Nothing, Settings.Get(Of Integer)("LoginPageType"), Settings.Get(Of Integer)("VersionServerLogin", Instance:=McInstanceSelected))
        If LoginType = 5 AndAlso String.IsNullOrWhiteSpace(ServerBase) Then ServerBase = "https://littleskin.cn/api/yggdrasil"
        Dim Server As String = ServerBase.TrimEnd("/"c) & "/authserver"
        If FrmLoginAuth Is Nothing Then
            Return New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = Server, .UserName = "", .Password = "", .Description = "Authlib-Injector", .Type = McLoginType.Auth}
        Else
            Return New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = Server, .UserName = FrmLoginAuth.ComboName.Text.Replace("¨", "").Trim, .Password = FrmLoginAuth.TextPass.Password.Replace("¨", "").Trim, .Description = "Authlib-Injector", .Type = McLoginType.Auth}
        End If
    End Function
    Private Sub AuthServer_Changed(sender As Object, e As TextChangedEventArgs) Handles TextAuthServer.TextChanged
        If IsReloading OrElse AniControlEnabled <> 0 Then Return
        If McInstanceSelected Is Nothing Then
            Settings.Set("CacheAuthServerServer", TextAuthServer.Text)
        Else
            Settings.Set("VersionServerAuthServer", TextAuthServer.Text, Instance:=McInstanceSelected)
        End If
        Settings.Set("CacheAuthAccess", "")
    End Sub
    ''' <summary>
    ''' 当前页面的登录信息是否有效。
    ''' </summary>
    Public Shared Function IsVaild(LoginData As McLoginServer) As String
        If LoginData.UserName = "" Then Return "账号不能为空！"
        If LoginData.Password = "" Then Return "密码不能为空！"
        Return ""
    End Function
    Public Function IsVaild() As String
        Return IsVaild(GetLoginData())
    End Function

    '保存输入信息
    Private Sub ComboName_TextChanged(sender As Object, e As TextChangedEventArgs) Handles ComboName.TextChanged
        If sender.Text = "" Then TextPass.Password = ""
        If AniControlEnabled = 0 Then Settings.Set("CacheAuthAccess", "")  '迫使其不进行 Validate
    End Sub
    Private Sub TextPass_PasswordChanged(sender As Object, e As RoutedEventArgs) Handles TextPass.PasswordChanged
        If AniControlEnabled = 0 Then Settings.Set("CacheAuthAccess", "")
    End Sub
    Private Sub ComboName_SelectionChanged(sender As MyComboBox, e As SelectionChangedEventArgs) Handles ComboName.SelectionChanged
        If sender.SelectedIndex = -1 OrElse Not Settings.Get(Of Boolean)("LoginRemember") Then
            TextPass.Password = ""
        Else
            TextPass.Password = Settings.Get(Of String)("LoginAuthPass").ToString.Split("¨")(sender.SelectedIndex).Trim
        End If
    End Sub
    Private Sub CheckBoxChange(sender As MyCheckBox, e As Object) Handles CheckRemember.Change
        If AniControlEnabled = 0 Then Settings.Set(sender.Tag, sender.Checked)
    End Sub

    '链接处理
    Private Sub ComboName_TextChanged() Handles ComboName.TextChanged
        BtnLink.Content = If(ComboName.Text = "", "注册账号", "找回密码")
    End Sub
    Private Sub Btn_Click(sender As Object, e As EventArgs) Handles BtnLink.Click
        Dim RegisterAddress = If(McInstanceSelected IsNot Nothing, Settings.Get(Of String)("VersionServerAuthRegister", Instance:=McInstanceSelected), Settings.Get(Of String)("CacheAuthServerRegister"))
        Dim LoginType = If(McInstanceSelected Is Nothing, Settings.Get(Of Integer)("LoginPageType"), Settings.Get(Of Integer)("VersionServerLogin", Instance:=McInstanceSelected))
        If LoginType = 5 AndAlso String.IsNullOrWhiteSpace(RegisterAddress) Then RegisterAddress = "https://littleskin.cn/auth/register"
        If BtnLink.Content = "注册账号" Then
            OpenWebsite(RegisterAddress)
        Else
            Dim Website As String = RegisterAddress
            OpenWebsite(Website.Replace("/auth/register", "/auth/forgot"))
        End If
    End Sub
    '切换注册按钮可见性
    Private Sub ReloadRegisterButton() Handles Me.Loaded
        Dim Address As String = If(McInstanceSelected IsNot Nothing, Settings.Get(Of String)("VersionServerAuthRegister", Instance:=McInstanceSelected), Settings.Get(Of String)("CacheAuthServerRegister"))
        Dim LoginType = If(McInstanceSelected Is Nothing, Settings.Get(Of Integer)("LoginPageType"), Settings.Get(Of Integer)("VersionServerLogin", Instance:=McInstanceSelected))
        If LoginType = 5 AndAlso String.IsNullOrWhiteSpace(Address) Then Address = "https://littleskin.cn/auth/register"
        BtnLink.Visibility = If(String.IsNullOrEmpty(New ValidateHttp().Validate(Address)), Visibility.Visible, Visibility.Collapsed)
    End Sub

End Class
