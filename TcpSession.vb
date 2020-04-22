Imports System.Net.Sockets

Public Class TcpSession

    Public Event DataReceived(Bytes() As Byte, Id As Integer)
    Public Event Closed(Id As Integer)

    Public Const RECEIVE_BUFFER_SIZE As Integer = 6096
    Public Const WSAECONNRESET As Integer = 10054

    Public Property Client As TcpClient = Nothing
    Public Property Stream As NetworkStream = Nothing
    Public Property Id As Integer = 0
    Public Property RaiseClosedEvent As Boolean = True
    Private _Closing As Boolean = False

    Public Sub New(SessionClient As TcpClient, SessionStream As NetworkStream, SessionId As Integer)
        Me.Client = SessionClient
        Me.Stream = SessionStream
        Me.Id = SessionId
    End Sub

    Public Sub Receive()
        Do
            Try
                Dim DataSize As Integer = 0
                Dim Buffer(RECEIVE_BUFFER_SIZE - 1) As Byte
                If Not Client Is Nothing AndAlso Client.Connected AndAlso Not Stream Is Nothing Then
                    DataSize = Stream.Read(Buffer, 0, Buffer.Length)
                    Array.Resize(Buffer, DataSize)
                    If DataSize = 0 Then
                        Console.WriteLine("ID: " & Me.Id & " - Client closed connection")
                        Exit Do
                    Else
                        RaiseEvent DataReceived(Buffer, Me.Id)
                    End If
                End If
            Catch ioex As IO.IOException
                If Not _Closing Then
                    If Not ioex.InnerException Is Nothing AndAlso (ioex.InnerException.GetType Is GetType(Net.Sockets.SocketException)) AndAlso
                   DirectCast(ioex.InnerException, Net.Sockets.SocketException).NativeErrorCode = WSAECONNRESET Then
                        Console.WriteLine("ID: " & Me.Id & " - Connection closed by host")
                    Else
                        Console.WriteLine("ID: " & Me.Id & " - Error reading from network stream: " & ioex.Message)
                    End If
                End If
                Exit Do
            Catch ex As Exception
                If Not _Closing Then
                    Console.WriteLine("ID: " & Me.Id & " - Error reading from network stream: " & ex.Message)
                End If
                Exit Do
            End Try
        Loop
        Close()
    End Sub

    Public Sub Send(Data() As Byte)
        Try
            If Not Client Is Nothing AndAlso Client.Connected AndAlso Not Stream Is Nothing Then
                Me.Stream.Write(Data, 0, Data.Length)
            End If
        Catch ex As Exception
            Console.WriteLine("Session ID: " & Me.Id & " - Error sending data: " & ex.Message)
        End Try
    End Sub

    Public Sub Close()
        If Me.RaiseClosedEvent Then
            RaiseEvent Closed(Me.Id)
        End If
        Me.RaiseClosedEvent = False
        _Closing = True
        Try
            If Not Client Is Nothing Then
                Client.Close()
            End If
        Catch ex As Exception
            Debug.WriteLine("Error closing client: " & ex.Message)
        End Try
        Try
            If Not Stream Is Nothing Then
                Stream.Close()
            End If
        Catch ex As Exception
            Debug.WriteLine("Error closing stream: " & ex.Message)
        End Try
    End Sub


End Class
