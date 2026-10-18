using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace IndepenDesk;

/// <summary>
/// WindicatorPlus / IndepenDesk 只读状态桥服务端 (v1)。
/// 在专用命名管道上以字节模式单向推送当前虚拟桌面全量状态快照 (XML 单行)，
/// 绝不执行命令、绝不传输窗口标题与句柄，后台线程仅发送不可变缓存快照。
/// </summary>
internal sealed class IndicatorStateServer : IDisposable
{
    public const int MaxDocumentBytes = 32768; // 32 KiB 协议上限

    private readonly string _pipeName;
    private readonly int _pid;
    private readonly int _sessionId;
    private readonly string _instanceId;
    private readonly CancellationTokenSource _cts = new();

    private long _sequence;
    private volatile byte[] _cachedSnapshot = Array.Empty<byte>();
    private Task? _serverTask;
    private bool _disposed;

    public string PipeName => _pipeName;
    public string InstanceId => _instanceId;
    public long CurrentSequence => Interlocked.Read(ref _sequence);
    public byte[] CurrentSnapshotBytes => _cachedSnapshot;

    public IndicatorStateServer(string? customPipeName = null)
    {
        _pid = Environment.ProcessId;
        _sessionId = Process.GetCurrentProcess().SessionId;
        _instanceId = Guid.NewGuid().ToString("D");
        _pipeName = customPipeName ?? GetDefaultPipeName();

        // 启动后台管道监听与推送循环
        _serverTask = Task.Run(() => RunServerLoopAsync(_cts.Token));
    }

    /// <summary>
    /// 获取符合协议规范的默认命名管道名：
    /// WindicatorPlus.IndepenDesk.v1.<current Windows SID>.<Process.GetCurrentProcess().SessionId>
    /// </summary>
    public static string GetDefaultPipeName()
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? "UNKNOWN";
        int sessionId = Process.GetCurrentProcess().SessionId;
        return GetPipeName(sid, sessionId);
    }

    public static string GetPipeName(string sid, int sessionId) =>
        $"WindicatorPlus.IndepenDesk.v1.{sid}.{sessionId}";

    /// <summary>
    /// UI 线程生成全量快照并更新不可变缓存。
    /// 必须仅由 UI 线程调用；每次调用严格自增 sequence 并生成新快照。
    /// </summary>
    public void UpdateSnapshot(IReadOnlyList<SwitchInfo> monitorInfos)
    {
        if (_disposed) return;

        long seq = Interlocked.Increment(ref _sequence);
        string xml = FormatStateXml(_pid, _sessionId, _instanceId, seq, monitorInfos);
        byte[] bytes = Encoding.UTF8.GetBytes(xml);

        if (bytes.Length > MaxDocumentBytes)
        {
            throw new InvalidOperationException($"Snapshot XML size ({bytes.Length} bytes) exceeds protocol bound {MaxDocumentBytes} bytes.");
        }

        _cachedSnapshot = bytes;
    }

    /// <summary>
    /// 格式化单行 XML 协议文本，无窗口标题与句柄，满足单行 <= 32KiB 约束。
    /// </summary>
    public static string FormatStateXml(int pid, int sessionId, string instanceId, long sequence, IReadOnlyList<SwitchInfo> monitorInfos)
    {
        var sb = new StringBuilder(256);
        sb.Append("<state version=\"1\" pid=\"").Append(pid)
          .Append("\" session=\"").Append(sessionId)
          .Append("\" instance=\"").Append(instanceId)
          .Append("\" sequence=\"").Append(sequence)
          .Append("\">");

        if (monitorInfos != null)
        {
            for (int i = 0; i < monitorInfos.Count; i++)
            {
                var info = monitorInfos[i];
                sb.Append("<monitor device=\"").Append(EscapeXmlAttribute(info.Device))
                  .Append("\" ordinal=\"").Append(info.Ordinal)
                  .Append("\" index=\"").Append(info.LocalIndex)
                  .Append("\" count=\"").Append(info.LocalCount)
                  .Append("\" global=\"").Append(info.GlobalNumber)
                  .Append("\" />");
            }
        }

        sb.Append("</state>\n");
        return sb.ToString();
    }

    private static string EscapeXmlAttribute(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
    }

    /// <summary>
    /// 后台管道服务循环：单向服务端，仅发送缓存中的不可变快照，不触碰 manager。
    /// </summary>
    private async Task RunServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                byte[] data = _cachedSnapshot;
                if (data.Length > 0)
                {
                    await server.WriteAsync(data, 0, data.Length, ct).ConfigureAwait(false);
                    await server.FlushAsync(ct).ConfigureAwait(false);
                    // DisconnectNamedPipe discards unread data, so wait for the consumer
                    // with a hard per-connection bound; cancel/dispose must remain responsive.
                    try
                    {
                        await Task.Run(() => server.WaitForPipeDrain()).WaitAsync(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                    }
                    catch (TimeoutException) { }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                if (server != null)
                {
                    try
                    {
                        if (server.IsConnected)
                            server.Disconnect();
                    }
                    catch { }
                    server.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts.Cancel();
        }
        catch { }

        try
        {
            _serverTask?.Wait(500);
        }
        catch { }

        _cts.Dispose();
    }
}
