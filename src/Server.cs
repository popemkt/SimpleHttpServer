using System.ComponentModel;
using System.IO.Compression;
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
    await socket.SendAsync(response.ToBytes(Encoding.Default));
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
    var response = request.Method switch
    {
        Method.GET =>
            request.Path switch
            {
                null => new Response { StatusCode = 200, Protocol = request.Protocol },
                "/" => new Response { StatusCode = 200, Protocol = request.Protocol },
                _ when request.Path.StartsWith("/echo/") => HandleGetEchoRequest(request),
                _ when request.Path.StartsWith("/files/") => HandleGetFileRequest(request),
                _ when request.Headers.ContainsKey(Header.UserAgent) => new Response
                {
                    StatusCode = 200,
                    Protocol = request.Protocol,
                    Body = request.Headers["User-Agent"],
                }.SetHeader(Header.ContentType, "text/plain"),
                _ => new Response { StatusCode = 404, Protocol = request.Protocol },
            },
        Method.POST =>
            request.Path switch
            {
                _ when request.Path.StartsWith("/files/") => HandlePostFileRequest(request),
                _ => new Response { StatusCode = 404, Protocol = request.Protocol },
            },
        _ => throw new ArgumentOutOfRangeException()
    };

    //TODO can create context(with request + response) to pass around, for dealing with special cases like gzip above

    return response;
}

Response HandleGetEchoRequest(Request request)
{
    var response = new Response
    {
        StatusCode = 200,
        Protocol = request.Protocol,
        Body = request.Path.Substring(6),
    }.SetHeader(Header.ContentType, "text/plain");

    if (request.Headers.TryGetValue(Header.AcceptEncoding, out string? value) &&
        value.Split(',', StringSplitOptions.TrimEntries).Contains("gzip"))
    {
        response.SetHeader(Header.ContentEncoding, "gzip");
        response.RawBody = Gzip(response.Body);
    }

    return response;
}

byte[] Gzip(string input)
{
    using var inputBytes = new MemoryStream(Encoding.UTF8.GetBytes(input));
    using var result = new MemoryStream();
    using (var compressionStream = new GZipStream(result,
               CompressionMode.Compress))
    {
        inputBytes.CopyTo(compressionStream);
    }

    return result.ToArray();
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
        }.SetHeader(Header.ContentType, "application/octet-stream"),
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
    public Dictionary<string, string> Headers { get; } = new();
    public string Body { get; set; }
}

public enum Method
{
    GET,
    POST,
}

public class Response
{
    private byte[]? _rawBody;
    public byte[]? RawBody
    {
        get => IsBodyRaw ? _rawBody : Encoding.Default.GetBytes(Body);
        set => _rawBody = value;
    }
    public string Body { get; set; }
    public int StatusCode { get; set; }
    public string Protocol { get; set; }
    private bool IsBodyRaw => _rawBody is { Length: > 0 };
    public Dictionary<string, string> Headers { get; set; } = new();
    public Response SetHeader(string header, string value)
    {
        Headers[header] = value;
        return this;
    }

    public string GetHeader(string key)
    {
        return Headers.TryGetValue(key, out var value) ? value : null;
    }

    private string GetContentHeaders()
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrEmpty(Body) || IsBodyRaw)
        {
            Headers.TryGetValue(Header.ContentType, out var contentType);
            var length = contentType switch
            {
                _ when Headers.TryGetValue(Header.ContentEncoding, out var contentEncoding) && contentEncoding == "gzip" => RawBody?.Length ?? 0,
                null => Body.Length,
                "application/octet-stream" => Encoding.UTF8.GetBytes(Body).Length,
                _ => throw new ArgumentOutOfRangeException(),
            };
        
            Headers[Header.ContentLength] = length.ToString();
        }

        foreach (var header in Headers)
        {
            builder.Append($"{header.Key}: {header.Value}\r\n");
        }
        
        return builder.ToString();
    }

    public override string ToString()
    {
        return $"{ToStringNoBody()}{Body}";
    }

    private string ToStringNoBody()
    {
        var headers = GetContentHeaders();
        return $"{Protocol} {StatusCode} {StatusCodes.Description[StatusCode]}\r\n{headers}\r\n";
    }

    public byte[] ToBytes(Encoding encoding)
    {
        var noBody = encoding.GetBytes(ToStringNoBody());
        return IsBodyRaw 
            ? noBody.Concat(RawBody!).ToArray() 
            : noBody.Concat(encoding.GetBytes(Body)).ToArray();
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

public static class Header
{
    public const string ContentType = "Content-Type";
    public const string ContentEncoding = "Content-Encoding";
    public const string ContentLength = "Content-Length";
    public const string AcceptEncoding = "Accept-Encoding";
    public const string UserAgent = "User-Agent";
}