Imports System.Net

Module MainModule

    Private Const TUNNEL_SESSION_ID As Integer = 0
    Private Const DEFAULT_TUNNEL_PORT As Integer = 9966

    Private _AppName As String = My.Application.Info.AssemblyName & ".exe"
    Private _Closing As Boolean
    Private _LocalPort As Integer
    Private _TunnelListener As Sockets.TcpListener
    Private _TunnelStream As Sockets.NetworkStream
    Private _AllowCloseSignal As New System.Threading.AutoResetEvent(False)
    Private _TunnelPort As Integer = DEFAULT_TUNNEL_PORT
    Private _TunnelWriteLock As New Object
    Private _SessionListLock As New Object
    Private _SessionList As New Dictionary(Of Integer, TcpSession)
    Private _LatestId As Integer = 1
    Private _TunnelIpString As String = String.Empty
    Private _TunnelIpAddress As IPAddress
    Private _LocalEndPoint As IPEndPoint
    Private _IsServer As Boolean = False
    Private _CurrentBuffer() As Byte

    Sub Main()
        Console.WriteLine(Environment.NewLine & "Port Tunnel (version " & My.Application.Info.Version.Major & "." & My.Application.Info.Version.Minor & ")" & Environment.NewLine &
                                                "http://vbscrub.com" & Environment.NewLine)
        If Not TryParseCommandLineArgs() Then
            Exit Sub
        End If
        Try
            _LocalEndPoint = New IPEndPoint(IPAddress.Loopback, _LocalPort)
            Dim TunnelClient As Sockets.TcpClient
            If _IsServer Then
                'SERVER MODE
                Console.WriteLine("Running in server mode")
                Dim TunnelEndPoint As New IPEndPoint(_TunnelIpAddress, _TunnelPort)
                TunnelClient = New Sockets.TcpClient
                Console.WriteLine("Connecting to " & TunnelEndPoint.ToString)
                TunnelClient.Connect(TunnelEndPoint)
                Console.WriteLine("Successfully connected. Waiting for data...")
            Else
                'CLIENT MODE
                Console.WriteLine("Running in client mode")
                Dim TunnelEndPoint As New IPEndPoint(IPAddress.Any, _TunnelPort)
                Console.WriteLine("Setting up tunnel listener")
                _TunnelListener = New Sockets.TcpListener(TunnelEndPoint)
                _TunnelListener.Start()
                Console.WriteLine("Waiting for tunnel connection on port " & _TunnelPort & "...")
                TunnelClient = _TunnelListener.AcceptTcpClient()
                Console.WriteLine("Tunnel client connected: " & TunnelClient.Client.RemoteEndPoint.ToString)
                Console.WriteLine("Setting up local listener")
                Dim LocalListener As New Sockets.TcpListener(New IPEndPoint(IPAddress.Any, _LocalPort))
                LocalListener.Start()
                Dim LocalThread As New Threading.Thread(AddressOf LocalListen)
                LocalThread.IsBackground = True
                LocalThread.Start(LocalListener)
            End If
            _TunnelStream = TunnelClient.GetStream
            Dim TunnelThread As New Threading.Thread(AddressOf TunnelListen)
            TunnelThread.IsBackground = True
            TunnelThread.Start(TunnelClient)
            _AllowCloseSignal.WaitOne()
        Catch ex As Exception
            Console.WriteLine("Error setting up listeners: " & ex.Message)
        End Try
    End Sub

    Private Sub TunnelListen(Client As Object)
        Dim TunnelSession As New TcpSession(DirectCast(Client, Sockets.TcpClient), _TunnelStream, TUNNEL_SESSION_ID)
        AddHandler TunnelSession.DataReceived, AddressOf Tunnel_DataReceived
        AddHandler TunnelSession.Closed, AddressOf Tunnel_Closed
        TunnelSession.Receive()
    End Sub

    'Only one thread raises this event handler
    Private Sub Tunnel_DataReceived(ReceivedBytes As Byte(), Id As Integer) 'ignore ID for tunnel session
        Try
            Dim WorkingBytes() As Byte = Nothing
            If _CurrentBuffer Is Nothing Then
                WorkingBytes = ReceivedBytes
            Else
                ReDim WorkingBytes(_CurrentBuffer.Length + ReceivedBytes.Length - 1)
                Array.Copy(_CurrentBuffer, 0, WorkingBytes, 0, _CurrentBuffer.Length)
                Array.Copy(ReceivedBytes, 0, WorkingBytes, _CurrentBuffer.Length, ReceivedBytes.Length)
            End If
            Dim CurrentOffset As Integer = 0
            Do
                'If we don't have enough bytes to construct the header (so we can check packet length) then save what we have for next time
                If (CurrentOffset + TunnelPacketHeader.HeaderLength) > WorkingBytes.Length Then
                    ReDim _CurrentBuffer(WorkingBytes.Length - CurrentOffset - 1)
                    Array.Copy(WorkingBytes, CurrentOffset, _CurrentBuffer, 0, _CurrentBuffer.Length)
                    Exit Sub
                End If
                Dim HeaderBytes(TunnelPacketHeader.HeaderLength - 1) As Byte
                Array.Copy(WorkingBytes, CurrentOffset, HeaderBytes, 0, HeaderBytes.Length)
                Dim Header As TunnelPacketHeader = TunnelPacketHeader.FromBytes(HeaderBytes)
                'If the current packet is larger than the amount of bytes left in current event, copy the remaining bytes to saved buffer and exit
                If Header.TotalPacketLength > (WorkingBytes.Length - CurrentOffset) Then
                    ReDim _CurrentBuffer(WorkingBytes.Length - CurrentOffset - 1)
                    Array.Copy(WorkingBytes, CurrentOffset, _CurrentBuffer, 0, _CurrentBuffer.Length)
                    Exit Sub
                Else
                    'we have enough bytes in this event to complete the packet
                    Dim PacketBytes(Header.TotalPacketLength - 1) As Byte
                    Array.Copy(WorkingBytes, CurrentOffset, PacketBytes, 0, PacketBytes.Length)
                    Dim Packet As TunnelPacket = TunnelPacket.FromBytes(PacketBytes)
                    CurrentOffset += Packet.Header.TotalPacketLength
                    ProcessTunnelPacket(Packet)
                    'If we've reached the end of the bytes available in this event by completing a packet then clear the saved buffer and exit
                    If CurrentOffset >= WorkingBytes.Length Then
                        If Not _CurrentBuffer Is Nothing Then
                            Array.Clear(_CurrentBuffer, 0, _CurrentBuffer.Length)
                            _CurrentBuffer = Nothing
                        End If
                        Exit Do
                    End If
                End If
            Loop
        Catch ex As Exception
            Console.WriteLine("Error constructing tunnel packet data: " & ex.Message)
            _CurrentBuffer = Nothing
        End Try
    End Sub

    Private Sub ProcessTunnelPacket(ReceivedPacket As TunnelPacket)
        Try
            Select Case ReceivedPacket.Header.MessageType
                Case TunnelPacket.TransmissionType.NewClient 'Should only happen on server?
                    Try
                        If ReceivedPacket.Header.SessionId > _LatestId Then
                            _LatestId = ReceivedPacket.Header.SessionId + 1
                        End If
                        Console.WriteLine("New client connected at remote machine. Session ID " & ReceivedPacket.Header.SessionId)
                        Dim NewClient As New Sockets.TcpClient
                        NewClient.Connect(_LocalEndPoint)
                        Console.WriteLine("Successfully connected to " & _LocalEndPoint.ToString)
                        Dim Session As New TcpSession(NewClient, NewClient.GetStream, ReceivedPacket.Header.SessionId)
                        StartSession(Session)
                    Catch ex As Exception
                        Console.WriteLine("Error setting up new local connection: " & ex.Message)
                    End Try
                Case TunnelPacket.TransmissionType.DisconnectClient 'happens on both cient and server
                    Try
                        Console.WriteLine("Client disconnected at remote machine. Session ID " & ReceivedPacket.Header.SessionId)
                        Dim TargetSession As TcpSession = Nothing
                        SyncLock _SessionListLock
                            If _SessionList.ContainsKey(ReceivedPacket.Header.SessionId) Then
                                TargetSession = _SessionList(ReceivedPacket.Header.SessionId)
                            End If
                        End SyncLock
                        If TargetSession Is Nothing Then
                            Console.WriteLine("Disconnect request is for non existent session (ID " & ReceivedPacket.Header.SessionId & ")")
                        Else
                            TargetSession.RaiseClosedEvent = False
                            TargetSession.Close()
                        End If
                        RemoveSession(ReceivedPacket.Header.SessionId, False)
                    Catch ex As Exception
                        Console.WriteLine("Error sending data to session " & ReceivedPacket.Header.SessionId & " : " & ex.Message)
                    End Try
                Case TunnelPacket.TransmissionType.TunnelData 'happens on both cient and server
                    Try
                        Dim TargetSession As TcpSession = Nothing
                        SyncLock _SessionListLock
                            If _SessionList.ContainsKey(ReceivedPacket.Header.SessionId) Then
                                TargetSession = _SessionList(ReceivedPacket.Header.SessionId)
                            End If
                        End SyncLock
                        If TargetSession Is Nothing Then
                            Console.WriteLine("Received data for non existent session (" & ReceivedPacket.Header.SessionId & ")")
                        Else
                            TargetSession.Send(ReceivedPacket.Data)
                            Console.WriteLine("Received " & ReceivedPacket.Header.TotalPacketLength & " from remote machine. Passing on to local session " & TargetSession.Id)
                        End If
                    Catch ex As Exception
                        Console.WriteLine("Error sending data to session " & ReceivedPacket.Header.SessionId & " : " & ex.Message)
                    End Try
            End Select
        Catch ex As Exception
            Console.WriteLine("Unexpected error in tunnel session: " & ex.Message)
        End Try
    End Sub

    Private Sub Tunnel_Closed(Id As Integer)
        Console.WriteLine("Tunnel connection closed by remote machine")
        Close()
    End Sub

    Private Sub SendTunnelData(Packet As TunnelPacket)
        Try
            If Packet.Header.MessageType = TunnelPacket.TransmissionType.TunnelData AndAlso Packet.Data.Length > TunnelPacket.SEND_BUFFER_SIZE Then
                Dim CurrentIndex As Integer = 0
                Do
                    Dim BuffSize As Integer = TunnelPacket.SEND_BUFFER_SIZE
                    Dim LastChunk As Boolean = False
                    If (Packet.Data.Length - CurrentIndex) < TunnelPacket.SEND_BUFFER_SIZE Then
                        BuffSize = Packet.Data.Length - CurrentIndex
                        LastChunk = True
                    End If
                    Dim Buffer(BuffSize - 1) As Byte
                    Array.Copy(Packet.Data, CurrentIndex, Buffer, 0, Buffer.Length)
                    CurrentIndex += Buffer.Length
                    SendTunnelSinglePacket(New TunnelPacket(Packet.Header.SessionId, Packet.Header.MessageType, Buffer))
                    If LastChunk Then
                        Exit Do
                    End If
                Loop
            Else
                SendTunnelSinglePacket(Packet)
            End If
        Catch ex As Exception
            Console.WriteLine("Error sending data to tunnel: " & ex.Message)
            Close()
        End Try
    End Sub

    Private Sub SendTunnelSinglePacket(Packet As TunnelPacket)
        Dim Bytes() As Byte = Nothing
        Try
            Bytes = Packet.GetBytes
        Catch ex As Exception
            Console.WriteLine("Error converting tunnel packet to byte array: " & ex.Message)
            Exit Sub
        End Try
        SyncLock _TunnelWriteLock
            _TunnelStream.Write(Bytes, 0, Bytes.Length)
        End SyncLock
        Console.WriteLine("Sent " & Bytes.Length & " bytes to remote machine")
    End Sub

    Private Sub StartSession(Session As TcpSession)
        AddHandler Session.DataReceived, AddressOf Session_DataReceived
        AddHandler Session.Closed, AddressOf Session_Closed
        SyncLock _SessionListLock
            _SessionList.Add(Session.Id, Session)
        End SyncLock
        Dim ReceiverThread As New Threading.Thread(AddressOf Session.Receive)
        ReceiverThread.IsBackground = True
        ReceiverThread.Start()
    End Sub

    Private Sub LocalListen(Listener As Object)
        Do Until _Closing
            Try
                Console.WriteLine("Waiting for local connection on port " & _LocalPort & "...")
                Dim NewClient As Sockets.TcpClient = DirectCast(Listener, Sockets.TcpListener).AcceptTcpClient
                Dim Session As New TcpSession(NewClient, NewClient.GetStream, Threading.Interlocked.Increment(_LatestId))
                Console.WriteLine("Local client connected, session ID = " & Session.Id)
                SendTunnelData(New TunnelPacket(Session.Id, TunnelPacket.TransmissionType.NewClient))
                StartSession(Session)
            Catch ex As Exception
                Console.WriteLine("Error accepting new local connection: " & ex.Message)
            End Try
            Threading.Thread.Sleep(60)
        Loop
    End Sub

    Private Sub Session_Closed(Id As Integer)
        RemoveSession(Id, True)
    End Sub

    Private Sub RemoveSession(Id As Integer, NotifyTunnel As Boolean)
        Try
            SyncLock _SessionListLock
                If _SessionList.Keys.Contains(Id) Then
                    _SessionList.Remove(Id)
                End If
            End SyncLock
            If NotifyTunnel Then
                SendTunnelData(New TunnelPacket(Id, TunnelPacket.TransmissionType.DisconnectClient))
            End If
        Catch ex As Exception
            Console.WriteLine("Error removing session " & Id & " from session list: " & ex.Message)
        End Try
    End Sub

    Private Sub Session_DataReceived(Data() As Byte, SessionId As Integer)
        Try
            If Not _TunnelStream Is Nothing Then
                SendTunnelData(New TunnelPacket(SessionId, TunnelPacket.TransmissionType.TunnelData, Data))
            End If
        Catch ex As Exception
            Console.WriteLine("ID: " & SessionId & " - Error sending data to tunnel: " & ex.Message)
        End Try
    End Sub

    Private Sub Close()
        _Closing = True
        Console.WriteLine("Closing down...")
        Try
            Dim LockSuccessful As Boolean = Threading.Monitor.TryEnter(_SessionListLock, TimeSpan.FromSeconds(30))
            Try
                For i As Integer = _SessionList.Count - 1 To 0 Step -1
                    Try
                        Dim Id As Integer = _SessionList(_SessionList.Keys(i)).Id
                        _SessionList(Id).RaiseClosedEvent = False
                        _SessionList(Id).Close()
                        If _SessionList.ContainsKey(Id) Then
                            _SessionList.Remove(Id)
                        End If
                    Catch ex As Exception
                        Console.WriteLine("Error closing session: " & ex.Message)
                    End Try
                Next
            Finally
                If LockSuccessful Then
                    Threading.Monitor.Exit(_SessionListLock)
                End If
            End Try
        Catch ex As Exception
            Console.WriteLine("Error closing sessions: " & ex.Message)
        End Try
        _AllowCloseSignal.Set()
    End Sub


    Private Sub WriteHelpText()
        Console.WriteLine(Environment.NewLine & "Forwards network traffic from the local machine to a remote machine also running this program, bypassing firewalls and making the traffic appear to originate locally to the remote machine. " & Environment.NewLine & Environment.NewLine &
                                 "Usage (client) : " & _AppName & " -p <port> [-c <port>]" & Environment.NewLine & Environment.NewLine &
                                 "Usage (server) : " & _AppName & " -s <ip> -p <port> [-c <port>]" & Environment.NewLine & Environment.NewLine &
                                 "-p <port>" & vbTab & "In client mode (no -s option specified) this is the port on the" & Environment.NewLine &
                                       vbTab & vbTab & "local machine to listen for traffic on, which will then be" & Environment.NewLine &
                                       vbTab & vbTab & "forwarded to the remote server." & Environment.NewLine &
                                       vbTab & vbTab & "In server mode (-s option) this is the port that will be" & Environment.NewLine &
                                       vbTab & vbTab & "opened up to receive traffic from the remote machine specified" & Environment.NewLine &
                                       vbTab & vbTab & "by the -s option" & Environment.NewLine & Environment.NewLine &
                                 "-s <ip>" & vbTab & vbTab & "Enables server mode and specifies the IP address of the remote" & Environment.NewLine &
                                       vbTab & vbTab & "machine to connect to and receive forwarded traffic from" & Environment.NewLine & Environment.NewLine &
                                 "-c <port>" & vbTab & "The port to be used for tunnel communication. Defaults to" & Environment.NewLine &
                                       vbTab & vbTab & DEFAULT_TUNNEL_PORT & " if not specified" & Environment.NewLine &
                                       Environment.NewLine & Environment.NewLine &
                                 "Example: " & Environment.NewLine & Environment.NewLine &
                                 "On the remote machine you want to expose the port from: " & Environment.NewLine & Environment.NewLine &
                                 _AppName & " -s 192.168.0.10 -p 5985" & Environment.NewLine & Environment.NewLine &
                                 "On your own machine: " & Environment.NewLine & Environment.NewLine &
                                 _AppName & " -p 5985" & Environment.NewLine & Environment.NewLine &
                                 "After running these commands, any connections to port 5985 on your own machine" & Environment.NewLine &
                                 "will be sent to the remote machine on port 5985 and will appear to originate" & Environment.NewLine &
                                 "locally to the remote machine" & Environment.NewLine)
    End Sub

    Private Function TryParseCommandLineArgs() As Boolean
        If My.Application.CommandLineArgs.Count = 0 OrElse My.Application.CommandLineArgs.Contains("/?") OrElse My.Application.CommandLineArgs.Contains("-h") Then
            WriteHelpText()
            Return False
        End If
        For i As Integer = 0 To My.Application.CommandLineArgs.Count - 1
            If String.Compare(My.Application.CommandLineArgs(i), "-p", True) = 0 AndAlso My.Application.CommandLineArgs.Count > (i) Then
                _LocalPort = CInt(My.Application.CommandLineArgs(i + 1))
                i += 1
            ElseIf String.Compare(My.Application.CommandLineArgs(i), "-c", True) = 0 AndAlso My.Application.CommandLineArgs.Count > (i) Then
                _TunnelPort = CInt(My.Application.CommandLineArgs(i + 1))
                i += 1
            ElseIf String.Compare(My.Application.CommandLineArgs(i), "-s", True) = 0 AndAlso My.Application.CommandLineArgs.Count > (i) Then
                _IsServer = True
                _TunnelIpString = My.Application.CommandLineArgs(i + 1)
                i += 1
            End If
        Next
        If _TunnelPort < 1 OrElse _TunnelPort > UInt16.MaxValue Then
            Console.WriteLine("Error: Invalid port specified with -c parameter")
            Return False
        End If
        If _LocalPort < 1 OrElse _LocalPort > UInt16.MaxValue Then
            Console.WriteLine("Error: Invalid port specified with -p parameter")
            Return False
        End If
        If _IsServer AndAlso (String.IsNullOrEmpty(_TunnelIpString) OrElse Not IPAddress.TryParse(_TunnelIpString, _TunnelIpAddress)) Then
            Console.WriteLine("Error: Invalid IP address specified with -s parameter")
            Return False
        End If
        Return True
    End Function



End Module
