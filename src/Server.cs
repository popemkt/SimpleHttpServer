using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;

// Uncomment this block to pass the first stage
var directory = args.SkipWhile(arg => arg != "--directory").Skip(1).FirstOrDefault() ?? ".";
TcpListener server = new TcpListener(IPAddress.Any, 4221);
server.Start();
while (true)
{
    var newSocket = await server.AcceptSocketAsync(); // wait for client
    _ = HandleSocket(newSocket);
}

async Task HandleSocket(Socket socket)
{
    var buffer = new byte[1024];
    var bytesReceived = await socket.ReceiveAsync(buffer, SocketFlags.None);
    var request = ParseRequest(buffer.AsSpan(0, bytesReceived));
    var response = HandleRequest(request);
    await socket.SendAsync(Encoding.Default.GetBytes(response.ToString()));
    socket.Close();
}

Request ParseRequest(ReadOnlySpan<byte> buffer)
{
    var parsedRequest = new Request();
    var state = ParsingState.Method;
    var headerName = "";
    var headerValue = "";
    var bodyBuilder = new List<byte>();
    var currentLine = new List<byte>();

    foreach (var b in buffer)
    {
        char c = (char)b;
        switch (state)
        {
            case ParsingState.Method:
                if (c == ' ')
                {
                    parsedRequest.Method = Enum.Parse<Method>(Encoding.ASCII.GetString(currentLine.ToArray()));
                    currentLine.Clear();
                    state = ParsingState.Path;
                }
                else
                {
                    currentLine.Add(b);
                }

                break;

            case ParsingState.Path:
                if (c == ' ')
                {
                    parsedRequest.Path = Encoding.ASCII.GetString(currentLine.ToArray());
                    currentLine.Clear();
                    state = ParsingState.Protocol;
                }
                else
                {
                    currentLine.Add(b);
                }

                break;

            case ParsingState.Protocol:
                if (c == '\r')
                {
                    parsedRequest.Protocol = Encoding.ASCII.GetString(currentLine.ToArray());
                    currentLine.Clear();
                    state = ParsingState.HeaderName;
                }
                else
                {
                    currentLine.Add(b);
                }

                break;

            case ParsingState.HeaderName:
                if (c == ':')
                {
                    headerName = Encoding.ASCII.GetString(currentLine.ToArray()).Trim();
                    currentLine.Clear();
                    state = ParsingState.HeaderValue;
                }
                else if (c == '\r')
                {
                    state = ParsingState.Body;
                }
                else
                {
                    currentLine.Add(b);
                }

                break;

            case ParsingState.HeaderValue:
                if (c == '\r')
                {
                    headerValue = Encoding.ASCII.GetString(currentLine.ToArray()).Trim();
                    parsedRequest.Headers[headerName] = headerValue;
                    currentLine.Clear();
                    state = ParsingState.HeaderName;
                }
                else
                {
                    currentLine.Add(b);
                }

                break;

            case ParsingState.Body:
                bodyBuilder.Add(b);
                break;
        }
    }

    parsedRequest.Body = Encoding.ASCII.GetString(bodyBuilder.ToArray()).TrimStart('\n');
    return parsedRequest;
}

Response HandleRequest(Request request)
{
    return request.Method switch
    {
        Method.GET =>
            request.Path switch
            {
                null => new Response { StatusCode = 200, Protocol = request.Protocol },
                "/" => new Response { StatusCode = 200, Protocol = request.Protocol },
                _ when request.Path.StartsWith("/echo/") => new Response
                {
                    StatusCode = 200,
                    Protocol = request.Protocol,
                    Body = request.Path.Substring(6),
                    ContentType = "text/plain"
                },
                _ when request.Path.StartsWith("/files/") => HandleGetFileRequest(request),
                _ when request.Headers.ContainsKey("User-Agent") => new Response
                {
                    StatusCode = 200,
                    Protocol = request.Protocol,
                    Body = request.Headers["User-Agent"],
                    ContentType = "text/plain"
                },
                _ => new Response { StatusCode = 404, Protocol = request.Protocol },
            },
        Method.POST =>
            request.Path switch
            {
                _ when request.Path.StartsWith("/files/") => HandlePostFileRequest(request),
                _ => new Response { StatusCode = 404, Protocol = request.Protocol },
            }
    };
}

Response HandlePostFileRequest(Request request)
{
    var fileName = request.Path.Substring(7);
    var filePath = Path.Combine(directory, fileName);
    File.WriteAllText(filePath, request.Body);
    return new Response { StatusCode = 201, Protocol = request.Protocol };
}


Response HandleGetFileRequest(Request request)
{
    var fileName = request.Path.Substring(7);
    var filePath = Path.Combine(directory, fileName);
    return File.Exists(filePath) switch
    {
        true => new Response
        {
            StatusCode = 200,
            Protocol = request.Protocol,
            Body = File.ReadAllText(filePath),
            ContentType = "application/octet-stream"
        },
        false => new Response { StatusCode = 404, Protocol = request.Protocol },
    };
}

public enum ParsingState
{
    Method,
    Path,
    Protocol,
    HeaderName,
    HeaderValue,
    Body
}

public class Request
{
    public string Path { get; set; }
    public Method Method { get; set; }
    public string Protocol { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Body { get; set; }
}

public enum Method
{
    GET,
    POST,
}

public class Response
{
    public string Body { get; set; }
    public int StatusCode { get; set; }
    public string Protocol { get; set; }
    public string ContentType { get; set; }

    private string GetContentHeaders()
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(ContentType))
            builder.Append($"Content-Type: {ContentType}\r\n");

        if (!string.IsNullOrEmpty(Body))
        {
            var length = ContentType == "application/octet-stream" ? Encoding.UTF8.GetByteCount(Body) : Body.Length;
            builder.Append($"Content-Length: {length}\r\n");
        }

        return builder.ToString();
    }

    public override string ToString()
    {
        var headers = GetContentHeaders();
        return $"{Protocol} {StatusCode} {StatusCodes.Description[StatusCode]}\r\n{headers}\r\n{Body}";
    }
}

public static class StatusCodes
{
    public static readonly Dictionary<int, string> Description = new()
    {
        { 200, "OK" },
        { 201, "Created" },
        { 404, "Not Found" },
        { 500, "Internal Server Error" }
    };
}