Public Class TunnelPacket

    'Data format: 
    'HEADER: Protocol code (7 bytes) | Message Length (4 byte int) | Transmission Type (1 byte) | SessionId (4 byte int) 
    'BODY: Data from client (size = buffer size - header length)

    Public Const SEND_BUFFER_SIZE As Integer = 6096

    Public Enum TransmissionType As Byte
        TunnelData = 0
        NewClient = 1
        DisconnectClient = 2
    End Enum

    Public Shared ProtocolCode() As Byte = New Byte() {12, 8, 31, 8, 35, 255, 255}

    Public Const LengthOffset As Integer = 7
    Public Const TypeOffset As Integer = 11
    Public Const SessionIdOffset As Integer = 12
   
    Public Property Header As New TunnelPacketHeader
    Public Property Data As Byte()

    Public Sub New(Id As Integer, MsgType As TransmissionType)
        Me.New(Id, MsgType, Nothing)
    End Sub

    Public Sub New(MsgHeader As TunnelPacketHeader, MsgData() As Byte)
        Me.Header = MsgHeader
        Me.Data = MsgData
        If MsgData Is Nothing Then
            Me.Header.TotalPacketLength = TunnelPacketHeader.HeaderLength
        Else
            Me.Header.TotalPacketLength = TunnelPacketHeader.HeaderLength + MsgData.Length
        End If
    End Sub

    Public Sub New(Id As Integer, MsgType As TransmissionType, MsgData As Byte())
        Me.Header.SessionId = Id
        Me.Header.MessageType = MsgType
        Me.Data = MsgData
        If MsgData Is Nothing Then
            Me.Header.TotalPacketLength = TunnelPacketHeader.HeaderLength
        Else
            Me.Header.TotalPacketLength = TunnelPacketHeader.HeaderLength + MsgData.Length
        End If
    End Sub

    Public Function GetBytes() As Byte()
        Dim Bytes() As Byte = Nothing
        If Me.Header.MessageType = TransmissionType.TunnelData Then
            If Me.Data Is Nothing OrElse Me.Data.Length = 0 Then
                Throw New ApplicationException("Tunnel data packet type but no data present")
            End If
            ReDim Bytes(TunnelPacketHeader.HeaderLength + Data.Length - 1)
        Else
            ReDim Bytes(TunnelPacketHeader.HeaderLength - 1)
        End If
        Array.Copy(ProtocolCode, 0, Bytes, 0, ProtocolCode.Length)
        Dim LengthBytes() As Byte = BitConverter.GetBytes(Me.Header.TotalPacketLength)
        Array.Copy(LengthBytes, 0, Bytes, LengthOffset, LengthBytes.Length)
        Bytes(TypeOffset) = Me.Header.MessageType
        Dim IdBytes() As Byte = BitConverter.GetBytes(Me.Header.SessionId)
        Array.Copy(IdBytes, 0, Bytes, SessionIdOffset, IdBytes.Length)
        If Me.Header.MessageType = TransmissionType.TunnelData AndAlso Not Me.Data Is Nothing Then
            Array.Copy(Me.Data, 0, Bytes, TunnelPacketHeader.HeaderLength, Me.Data.Length)
        End If
        Return Bytes
    End Function

    Public Shared Function FromBytes(Bytes() As Byte) As TunnelPacket
        Try
            If Not TunnelPacketHeader.ContainsProtocolCode(Bytes) Then
                Throw New ApplicationException("Data is not in correct format for tunnel protocol")
            End If
            Dim Header As TunnelPacketHeader = TunnelPacketHeader.FromBytes(Bytes)
            Dim DataBytes() As Byte = Nothing
            If Header.MessageType = TransmissionType.TunnelData Then
                ReDim DataBytes(Header.TotalPacketLength - TunnelPacketHeader.HeaderLength - 1)
                Array.Copy(Bytes, TunnelPacketHeader.HeaderLength, DataBytes, 0, DataBytes.Length)
            End If
            Return New TunnelPacket(Header, DataBytes)
        Catch ex As Exception
            Throw New ApplicationException("Error parsing tunnel packet: " & ex.Message)
        End Try
    End Function

   


End Class
