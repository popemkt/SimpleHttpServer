using System.Net;
using System.Net.Sockets;
using System.Text;

// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");

// Uncomment this block to pass the first stage
TcpListener server = new TcpListener(IPAddress.Any, 4221);
server.Start();
var socket = await server.AcceptSocketAsync(); // wait for client
var buffer = new byte[1024];
var request = socket.ReceiveAsync(buffer);
var response = "HTTP/1.1 200 OK\r\n\r\n";
await socket.SendAsync(Encoding.Default.GetBytes(response));
socket.Close();
