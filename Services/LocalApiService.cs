using System.IO;
using System.Net;
using System.Text;

namespace RobotFleetSimulation.Services;

public sealed class LocalApiService : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<string, string, string, string> _handler;
    private CancellationTokenSource? _stop;

    public LocalApiService(Func<string, string, string, string> handler)
    {
        _handler = handler;
        _listener.Prefixes.Add("http://127.0.0.1:5084/");
    }

    public void Start()
    {
        if (_listener.IsListening) return;
        _stop = new CancellationTokenSource();
        _listener.Start();
        _ = ListenAsync(_stop.Token);
    }

    private async Task ListenAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(token);
                _ = Task.Run(() => HandleAsync(context), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) when (token.IsCancellationRequested) { }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            string body = await reader.ReadToEndAsync();
            string result = _handler(context.Request.HttpMethod, context.Request.Url?.AbsolutePath ?? "/", body);
            byte[] bytes = Encoding.UTF8.GetBytes(result);
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
        }
        catch
        {
            context.Response.StatusCode = 500;
        }
        finally { context.Response.Close(); }
    }

    public void Dispose()
    {
        _stop?.Cancel();
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        _stop?.Dispose();
    }
}
