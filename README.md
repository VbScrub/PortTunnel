# PortTunnel

A simple port forwarding/tunnelling utility (think Chisel but simpler, if all you want to do is reverse port tunnelling). 

This lets you forward TCP connections from your own machine on to a remote machine, but sends the data over a separate "tunnel" that was created by the remote machine (bypassing inbound firewall rules on the remote machine, and allowing you to remotely access ports that are only listening for local connections). Full video explanation of port tunnelling concept here: https://www.youtube.com/watch?v=JDUrT3IEzLI

Still needs more testing and optimisation, but does the job for now.

Full documentation of usage here: https://vbscrub.com/tools/porttunnel-pt-exe

# Example

To allow remote access to port 5985 on a machine that is only listening locally or has firewall blocking it:

On your own machine that has an IP of 192.168.0.10:

`PT.exe -p 5985`

On the remote machine:

`PT.exe -s 192.168.0.10 -p 5985`

# Requires .NET Framework 4.0 (full or client profile)
