using System.ComponentModel;
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
var _ = socket.ReceiveAsync(buffer);
var decodedString = Encoding.Default.GetString(buffer);
var request = ExtractHandleRequestString(decodedString);
var response = ExtractHandleResponseString(request);
await socket.SendAsync(Encoding.Default.GetBytes(response.ToString()));
socket.Close();

Request ExtractHandleRequestString(string decodedString)
{
    var parsedRequest = new Request();
    var lines = decodedString.Split("\r\n");
    var parts = lines[0].Split(' ');
    parsedRequest.Path = parts[1];
    parsedRequest.Method = Enum.Parse<Method>(parts[0]);
    parsedRequest.Protocol = parts[2];

    return parsedRequest;
}

Response ExtractHandleResponseString(Request request)
{
    return request.Path switch
    {
        "/" => new Response { StatusCode = 200 , Protocol = request.Protocol},
        _ => new Response { StatusCode = 404, Protocol = request.Protocol},
    };
}

public class Request
{
    public string Path { get; set; }
    public Method Method { get; set; }
    public string Protocol { get; set; }
}

public enum Method
{
    GET,
}

public class Response
{
    public string Body { get; set; }
    public int StatusCode { get; set; }
    public string Protocol { get; set; }
    public string ContentType { get; set; }
    public Method Method { get; set; }

    public override string ToString()
    {
        return $"{Protocol} {StatusCode} {StatusCodes.Description[StatusCode]}\r\n\r\n{Body}";
    }
}

public static class StatusCodes
{
    public static readonly Dictionary<int, string> Description = new()
    {
        { 200, "OK" },
        { 404, "Not Found" },
        { 500, "Internal Server Error" }
    };
}