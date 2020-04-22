Imports PortTunnel.TunnelPacket

Public Class TunnelPacketHeader

    Public Shared Property HeaderLength As Integer = 16
    Public Property TotalPacketLength As Integer
    Public Property SessionId As Integer
    Public Property MessageType As TransmissionType

    Public Shared Function FromBytes(Bytes() As Byte) As TunnelPacketHeader
        If Not ContainsProtocolCode(Bytes) Then
            Throw New ApplicationException("Data is not in correct format for tunnel protocol")
        End If
        Dim Lngth As Integer = BitConverter.ToInt32(Bytes, LengthOffset)
        Dim MsgType As TransmissionType = DirectCast(Bytes(TypeOffset), TransmissionType)
        Dim Id As Integer = BitConverter.ToInt32(Bytes, SessionIdOffset)
        Return New TunnelPacketHeader With {.MessageType = MsgType, .SessionId = Id, .TotalPacketLength = Lngth}
    End Function

    Public Shared Function ContainsProtocolCode(Bytes() As Byte) As Boolean
        If Bytes Is Nothing OrElse Bytes.Length < TunnelPacketHeader.HeaderLength Then 'make sure packet is at least as long as the header
            Return False
        End If
        Dim ProtocolCodeFound As Boolean = True
        For i As Integer = 0 To ProtocolCode.Length - 1
            If Not Bytes(i) = ProtocolCode(i) Then
                ProtocolCodeFound = False
                Exit For
            End If
        Next
        If Not ProtocolCodeFound Then
            Return False
        End If
        Return True
    End Function

End Class
