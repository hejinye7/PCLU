Imports System.IO
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports Newtonsoft.Json.Linq

Friend Module ModTerracotta

    Private Const TerracottaVersion As String = "0.4.2"
    Private Const PackageSha512 As String = "6a98f524d4f00373696517306af8aa50d01d55ce4eadb27e9e4bc2f882707a0b5f20d5d4c33371d1459dcf5bf144ffed9beb414202d9ccf32b11dbbfcf19d650"
    Private Const CodeChars As String = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ"
    Private TerracottaPort As Integer = -1
    Private PortFile As String = ""

    Friend Class TerracottaSnapshot
        Friend Ok As Boolean
        Friend Stage As String = ""
        Friend Room As String = ""
        Friend Url As String = ""
        Friend Difficulty As String = ""
        Friend ExceptionType As Integer = -1
    End Class

    Friend ReadOnly Property InstallDirectory As String
        Get
            Return Path.Combine(PathPure.Value, "Terracotta") & "\"
        End Get
    End Property

    Friend ReadOnly Property ExecutablePath As String
        Get
            Try
                If Not Directory.Exists(InstallDirectory) Then Return ""
                Return Directory.GetFiles(InstallDirectory, "terracotta*.exe").FirstOrDefault()
            Catch ex As Exception
                Logger.Warn(ex, "查找陶瓦程序失败")
                Return ""
            End Try
        End Get
    End Property

    Friend ReadOnly Property IsInstalled As Boolean
        Get
            Return ExecutablePath <> ""
        End Get
    End Property

    Private Function HttpGet(Port As Integer, ApiPath As String, Optional TimeoutMs As Integer = 4000) As String
        Using Handler As New HttpClientHandler With {.UseProxy = False}
            Using Client As New HttpClient(Handler) With {.Timeout = TimeSpan.FromMilliseconds(TimeoutMs)}
                Return Client.GetStringAsync($"http://127.0.0.1:{Port}{ApiPath}").GetAwaiter().GetResult()
            End Using
        End Using
    End Function

    Private Function LockPort() As Integer
        Try
            Dim LockPath As String = Path.Combine(Path.GetTempPath(), "terracotta", "terracotta.lock")
            If Not File.Exists(LockPath) Then Return -1
            Using Stream As New FileStream(LockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Dim Bytes(1) As Byte
                If Stream.Read(Bytes, 0, 2) < 2 Then Return -1
                Return (CInt(Bytes(0)) << 8) Or CInt(Bytes(1))
            End Using
        Catch
            Return -1
        End Try
    End Function

    Private Function IsAlive(Port As Integer) As Boolean
        If Port <= 0 Then Return False
        Try
            Return HttpGet(Port, "/meta", 1500).Contains("version")
        Catch
            Return False
        End Try
    End Function

    Friend Function RunningPort() As Integer
        If TerracottaPort > 0 AndAlso IsAlive(TerracottaPort) Then Return TerracottaPort
        Dim ExistingPort As Integer = LockPort()
        If IsAlive(ExistingPort) Then
            TerracottaPort = ExistingPort
            Return ExistingPort
        End If
        Return -1
    End Function

    Friend Function EnsureRunning() As Integer
        Dim ExistingPort As Integer = RunningPort()
        If ExistingPort > 0 Then Return ExistingPort
        Dim ExePath As String = ExecutablePath
        If ExePath = "" Then Return -1

        Try
            If PortFile = "" Then PortFile = Path.Combine(Path.GetTempPath(), "PCL-Terracotta-Port.json")
            If File.Exists(PortFile) Then File.Delete(PortFile)
            Process.Start(New ProcessStartInfo(ExePath) With {
                .Arguments = "--hmcl """ & PortFile & """",
                .UseShellExecute = False,
                .WorkingDirectory = Path.GetDirectoryName(ExePath)
            })
            For Index As Integer = 1 To 100
                Thread.Sleep(200)
                If Not File.Exists(PortFile) Then Continue For
                Dim Match = Text.RegularExpressions.Regex.Match(File.ReadAllText(PortFile), """port""\s*:\s*(\d+)")
                If Not Match.Success Then Continue For
                Dim NewPort As Integer = CInt(Match.Groups(1).Value)
                If IsAlive(NewPort) Then
                    TerracottaPort = NewPort
                    Logger.Info($"陶瓦联机已启动，端口 {NewPort}")
                    Return NewPort
                End If
            Next
            Logger.Warn("陶瓦联机启动超时")
        Catch ex As Exception
            Logger.Error(ex, "启动陶瓦联机失败")
        End Try
        Return -1
    End Function

    Friend Function ReadState(Optional AutoStart As Boolean = False) As TerracottaSnapshot
        Dim Result As New TerracottaSnapshot
        Dim Port As Integer = If(AutoStart, EnsureRunning(), RunningPort())
        If Port <= 0 Then Return Result
        Try
            Dim Json = JObject.Parse(HttpGet(Port, "/state"))
            Result.Ok = True
            Result.Stage = If(Json("state")?.ToString(), "")
            Result.Room = If(Json("room")?.ToString(), "")
            Result.Url = If(Json("url")?.ToString(), "")
            Result.Difficulty = If(Json("difficulty")?.ToString(), "")
            If Json("type") IsNot Nothing Then Integer.TryParse(Json("type").ToString(), Result.ExceptionType)
        Catch
            TerracottaPort = -1
        End Try
        Return Result
    End Function

    Private Sub EnsureIdle(Port As Integer)
        Try
            HttpGet(Port, "/state/ide", 2500)
        Catch ex As Exception
            Logger.Warn(ex, "复位陶瓦状态失败")
        End Try
    End Sub

    Friend Function StartHost() As String
        Dim Port As Integer = EnsureRunning()
        If Port <= 0 Then Return "陶瓦尚未安装或无法启动。"
        EnsureIdle(Port)
        Try
            HttpGet(Port, "/state/scanning", 5000)
            Return "已开始建房。请在游戏中对局域网开放，房间码会自动显示。"
        Catch ex As Exception
            Logger.Warn(ex, "请求陶瓦建房失败")
            Return "建房失败：" & ex.Message
        End Try
    End Function

    Friend Function JoinRoom(RoomCode As String) As String
        Dim Code As String = NormalizeCode(RoomCode)
        If Code = "" OrElse Not CheckCode(Code) Then Return "房间码无效，应为 U/XXXX-XXXX-XXXX-XXXX。"
        Dim Port As Integer = EnsureRunning()
        If Port <= 0 Then Return "陶瓦尚未安装或无法启动。"
        EnsureIdle(Port)
        Try
            HttpGet(Port, "/state/guesting?room=" & Uri.EscapeDataString(Code), 5000)
            Return $"正在加入 {Code}。连上后到游戏的多人游戏列表进入陶瓦联机大厅。"
        Catch ex As Exception
            Logger.Warn(ex, "请求陶瓦加入房间失败")
            Return "加入失败：" & ex.Message
        End Try
    End Function

    Friend Sub ResetState()
        Dim Port As Integer = RunningPort()
        If Port > 0 Then EnsureIdle(Port)
    End Sub

    Friend Sub Shutdown()
        Dim Port As Integer = RunningPort()
        If Port <= 0 Then Return
        Try
            HttpGet(Port, "/panic?peaceful=true", 2000)
        Catch
            '陶瓦先退出进程，因此连接重置属于正常结果。
        End Try
        TerracottaPort = -1
    End Sub

    Friend Function StatusText(Snapshot As TerracottaSnapshot) As String
        If Not IsInstalled Then Return "尚未安装陶瓦，首次创建或加入房间时会自动安装。"
        If Snapshot Is Nothing OrElse Not Snapshot.Ok Then Return "陶瓦未运行。"
        Select Case Snapshot.Stage
            Case "waiting" : Return "空闲，可以创建或加入房间。"
            Case "host-scanning" : Return "等待游戏开启局域网。"
            Case "host-starting" : Return $"正在创建房间，房间码：{Snapshot.Room}"
            Case "host-ok" : Return $"房间已就绪，房间码：{Snapshot.Room}"
            Case "guest-connecting" : Return $"正在连接房间 {Snapshot.Room}。"
            Case "guest-starting" : Return "正在建立联机通道。"
            Case "guest-ok"
                Return If(Snapshot.Url = "", "已连接，进入游戏多人列表中的陶瓦联机大厅。", $"已连接。大厅不可用时可直接连接 {Snapshot.Url}")
            Case "exception"
                If Snapshot.Room = "" AndAlso Snapshot.Url = "" Then Return "陶瓦已启动，可以创建或加入房间。"
                Return "联机失败，请复位后重试并确认双方陶瓦版本一致。"
            Case Else : Return "当前状态：" & Snapshot.Stage
        End Select
    End Function

    Friend Function NormalizeCode(Code As String) As String
        If Code Is Nothing Then Return ""
        Dim Value As String = Code.Trim().ToUpperInvariant().Replace(" ", "").Replace("I", "1").Replace("O", "0")
        Dim Start As Integer = Value.IndexOf("U/", StringComparison.Ordinal)
        If Start < 0 OrElse Value.Length - Start < 21 Then Return ""
        Return Value.Substring(Start, 21)
    End Function

    Friend Function CheckCode(Code As String) As Boolean
        Dim Value As String = NormalizeCode(Code)
        If Value.Length <> 21 Then Return False
        Dim Body As String = Value.Substring(2)
        Dim Modulo As Integer = 0
        For Index As Integer = 0 To Body.Length - 1
            If Index = 4 OrElse Index = 9 OrElse Index = 14 Then
                If Body(Index) <> "-"c Then Return False
                Continue For
            End If
            Dim Digit As Integer = CodeChars.IndexOf(Body(Index))
            If Digit < 0 Then Return False
            Modulo = (Modulo * 34 + Digit) Mod 7
        Next
        Return Modulo = 0
    End Function

    Friend Function Install() As String
        Dim FileName As String = $"terracotta-{TerracottaVersion}-windows-x86_64-pkg.tar.gz"
        Dim Sources As String() = {
            $"https://cnb.cool/HMCL-Terracotta/Terracotta/-/releases/download/v{TerracottaVersion}/{FileName}",
            $"https://github.com/burningtnt/Terracotta/releases/download/v{TerracottaVersion}/{FileName}"
        }
        Directory.CreateDirectory(InstallDirectory)
        Dim PackagePath As String = Path.Combine(InstallDirectory, FileName)
        Dim LastError As String = "未知错误"
        Shutdown()
        Thread.Sleep(300)

        For Each Source As String In Sources
            Try
                Logger.Info("下载陶瓦联机：" & Source)
                Using Client As New HttpClient With {.Timeout = TimeSpan.FromMinutes(3)}
                    File.WriteAllBytes(PackagePath, Client.GetByteArrayAsync(Source).GetAwaiter().GetResult())
                End Using
                If ComputeSha512(PackagePath) <> PackageSha512 Then Throw New InvalidDataException("SHA-512 校验失败")
                ExtractTarGz(PackagePath, InstallDirectory)
                File.Delete(PackagePath)
                If ExecutablePath = "" Then Throw New FileNotFoundException("压缩包中没有找到陶瓦程序")
                Return $"陶瓦联机 {TerracottaVersion} 安装完成。"
            Catch ex As Exception
                LastError = ex.Message
                Logger.Warn(ex, "下载陶瓦联机失败，尝试下一个源")
            End Try
        Next
        Return "陶瓦安装失败：" & LastError
    End Function

    Private Function ComputeSha512(FilePath As String) As String
        Using Input As FileStream = File.OpenRead(FilePath), Hasher As SHA512 = SHA512.Create()
            Dim Builder As New StringBuilder
            For Each Value As Byte In Hasher.ComputeHash(Input)
                Builder.Append(Value.ToString("x2"))
            Next
            Return Builder.ToString()
        End Using
    End Function

    Private Sub ExtractTarGz(PackagePath As String, Destination As String)
        Using Input As FileStream = File.OpenRead(PackagePath)
            Using Gzip As New Compression.GZipStream(Input, Compression.CompressionMode.Decompress)
                Dim Header(511) As Byte
                Do
                    If ReadFull(Gzip, Header, 512) < 512 OrElse IsEmptyHeader(Header) Then Exit Do
                    Dim Name As String = TarText(Header, 0, 100)
                    Dim SizeText As String = TarText(Header, 124, 12).Trim()
                    Dim Size As Long = If(SizeText = "", 0, Convert.ToInt64(SizeText, 8))
                    Dim OutputPath As String = If(Name = "", "", Path.Combine(Destination, Path.GetFileName(Name)))
                    Dim Remaining As Long = Size
                    If OutputPath <> "" AndAlso (Header(156) = 0 OrElse Header(156) = AscW("0"c)) Then
                        Using Output As New FileStream(OutputPath, FileMode.Create, FileAccess.Write)
                            CopyBytes(Gzip, Output, Remaining)
                        End Using
                    Else
                        CopyBytes(Gzip, Nothing, Remaining)
                    End If
                    Dim Padding As Integer = CInt((512 - (Size Mod 512)) Mod 512)
                    If Padding > 0 Then
                        Dim Skip(Padding - 1) As Byte
                        ReadFull(Gzip, Skip, Padding)
                    End If
                Loop
            End Using
        End Using
    End Sub

    Private Sub CopyBytes(Input As Stream, Output As Stream, Count As Long)
        Dim Buffer(8191) As Byte
        While Count > 0
            Dim Read As Integer = Input.Read(Buffer, 0, CInt(Math.Min(Count, Buffer.Length)))
            If Read <= 0 Then Throw New EndOfStreamException()
            If Output IsNot Nothing Then Output.Write(Buffer, 0, Read)
            Count -= Read
        End While
    End Sub

    Private Function ReadFull(Input As Stream, Buffer() As Byte, Count As Integer) As Integer
        Dim Total As Integer = 0
        While Total < Count
            Dim Read As Integer = Input.Read(Buffer, Total, Count - Total)
            If Read <= 0 Then Exit While
            Total += Read
        End While
        Return Total
    End Function

    Private Function IsEmptyHeader(Header() As Byte) As Boolean
        For Each Value As Byte In Header
            If Value <> 0 Then Return False
        Next
        Return True
    End Function

    Private Function TarText(Buffer() As Byte, Offset As Integer, Length As Integer) As String
        Dim Last As Integer = Offset + Length - 1
        While Last >= Offset AndAlso Buffer(Last) = 0
            Last -= 1
        End While
        If Last < Offset Then Return ""
        Return Encoding.UTF8.GetString(Buffer, Offset, Last - Offset + 1)
    End Function

End Module
