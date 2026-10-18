using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Xml;

namespace WindicatorPlus
{
    public class IndepenDeskMonitorInfo
    {
        public string Device { get; private set; }
        public int Ordinal { get; private set; }
        public int Index { get; private set; }
        public int Count { get; private set; }
        public int Global { get; private set; }

        public IndepenDeskMonitorInfo(string device, int ordinal, int index, int count, int global)
        {
            Device = device;
            Ordinal = ordinal;
            Index = index;
            Count = count;
            Global = global;
        }
    }

    public class IndepenDeskSnapshot
    {
        public int Version { get; private set; }
        public int Pid { get; private set; }
        public int Session { get; private set; }
        public string Instance { get; private set; }
        public long Sequence { get; private set; }
        public ReadOnlyCollection<IndepenDeskMonitorInfo> Monitors { get; private set; }

        public IndepenDeskSnapshot(int version, int pid, int session, string instance, long sequence, IList<IndepenDeskMonitorInfo> monitors)
        {
            Version = version;
            Pid = pid;
            Session = session;
            Instance = instance;
            Sequence = sequence;
            List<IndepenDeskMonitorInfo> copy = monitors != null
                ? new List<IndepenDeskMonitorInfo>(monitors)
                : new List<IndepenDeskMonitorInfo>();
            Monitors = new ReadOnlyCollection<IndepenDeskMonitorInfo>(copy);
        }
    }

    public class SnapshotEventArgs : EventArgs
    {
        public IndepenDeskSnapshot Snapshot { get; private set; }
        public SnapshotEventArgs(IndepenDeskSnapshot snapshot)
        {
            Snapshot = snapshot;
        }
    }

    /// <summary>
    /// 只读监听 IndepenDesk 状态的 Windows 命名管道客户端。
    /// 严格遵循 STATE-PROTOCOL 规范：
    /// 1. 纯后台工作线程轮询，不阻塞 UI 线程。
    /// 2. 连接上限 200ms，读取上限 600ms 与 32KiB。
    /// 3. XML 解析安全加固（禁止 DTD / XmlResolver 置空）。
    /// 4. 单调时钟 3 秒失效判定，旧/重复 sequence 不刷新有效度。
    /// 5. 产出完全不可变的状态快照。
    /// </summary>
    public class IndepenDeskClient : IDisposable
    {
        private readonly string _pipeName;
        private readonly int _expectedSessionId;
        private readonly object _stateLock = new object();
        private readonly Func<long> _timeProvider;

        private Thread _workerThread;
        private volatile bool _isRunning;
        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);

        private IndepenDeskSnapshot _currentSnapshot;
        private string _currentInstance;
        private long _currentSequence = -1;
        private readonly Stopwatch _freshnessStopwatch = new Stopwatch();
        private long _lastSnapshotTimeMs = -1;
        private bool _isConnected;
        private bool _lastStaleReported = true;

        public event EventHandler<SnapshotEventArgs> SnapshotReceived;
        public event EventHandler StateChanged;

        public IndepenDeskClient(string pipeNameOverride = null, int? expectedSessionIdOverride = null, Func<long> timeProviderOverride = null)
        {
            _expectedSessionId = expectedSessionIdOverride.HasValue
                ? expectedSessionIdOverride.Value
                : Process.GetCurrentProcess().SessionId;

            _timeProvider = timeProviderOverride;

            if (!string.IsNullOrEmpty(pipeNameOverride))
            {
                _pipeName = pipeNameOverride;
            }
            else
            {
                string sid = GetCurrentWindowsSid();
                _pipeName = string.Format("WindicatorPlus.IndepenDesk.v1.{0}.{1}", sid, _expectedSessionId);
            }
        }

        public string PipeName
        {
            get { return _pipeName; }
        }

        public int ExpectedSessionId
        {
            get { return _expectedSessionId; }
        }

        public bool IsConnected
        {
            get
            {
                lock (_stateLock)
                {
                    return _isConnected && !IsStale;
                }
            }
        }

        public bool IsStale
        {
            get
            {
                lock (_stateLock)
                {
                    if (_currentSnapshot == null)
                        return true;

                    if (_timeProvider != null)
                    {
                        if (_lastSnapshotTimeMs < 0) return true;
                        return (_timeProvider() - _lastSnapshotTimeMs) > 3000;
                    }

                    if (!_freshnessStopwatch.IsRunning)
                        return true;

                    return _freshnessStopwatch.ElapsedMilliseconds > 3000;
                }
            }
        }

        public IndepenDeskSnapshot CurrentSnapshot
        {
            get
            {
                lock (_stateLock)
                {
                    return _currentSnapshot;
                }
            }
        }

        public static string GetCurrentWindowsSid()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    if (identity != null && identity.User != null)
                    {
                        return identity.User.Value;
                    }
                }
            }
            catch { }
            return "unknown-sid";
        }

        public void Start()
        {
            lock (_stateLock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _stopEvent.Reset();
                _workerThread = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "IndepenDeskClientWorker"
                };
                _workerThread.Start();
            }
        }

        public void Stop()
        {
            lock (_stateLock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                _stopEvent.Set();
            }

            if (_workerThread != null && _workerThread.IsAlive)
            {
                _workerThread.Join(2000);
            }
        }

        private void WorkerLoop()
        {
            while (_isRunning)
            {
                try
                {
                    TryProbeOnce();
                }
                catch
                {
                    // 管道探测保护，杜绝异常逃逸
                }

                // 检查新鲜度时钟是否超时
                bool currentStale = IsStale;
                if (currentStale != _lastStaleReported)
                {
                    _lastStaleReported = currentStale;
                    OnStateChanged();
                }

                // 每 600ms 探测一次
                if (_stopEvent.WaitOne(600))
                {
                    break;
                }
            }
        }

        internal string LastProbeError { get; private set; }

        public bool TryProbeOnce()
        {
            NamedPipeClientStream pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.In, PipeOptions.Asynchronous);
                
                // Connect 必须 <= 200ms
                try
                {
                    pipe.Connect(200);
                }
                catch (TimeoutException)
                {
                    SetConnected(false);
                    return false;
                }
                catch (IOException)
                {
                    SetConnected(false);
                    return false;
                }

                // 读取必须 <= 600ms 且 <= 32768 字节
                byte[] rawBuffer = ReadWithBoundedTimeAndSize(pipe, 600, 32768);
                if (rawBuffer == null || rawBuffer.Length == 0)
                {
                    LastProbeError = "Read empty or timed out";
                    SetConnected(false);
                    return false;
                }

                string error;
                IndepenDeskSnapshot snapshot = ParseXmlBytes(rawBuffer, _expectedSessionId, out error);
                if (snapshot == null)
                {
                    LastProbeError = "Invalid XML: " + error;
                    return false;
                }

                bool updated = ProcessSnapshot(snapshot);
                if (updated)
                {
                    SetConnected(true);
                    OnSnapshotReceived(snapshot);
                }

                return updated;
            }
            catch (Exception ex)
            {
                LastProbeError = ex.ToString();
                SetConnected(false);
                return false;
            }
            finally
            {
                if (pipe != null)
                {
                    try { pipe.Close(); } catch { }
                    try { pipe.Dispose(); } catch { }
                }
            }
        }

        private static byte[] ReadWithBoundedTimeAndSize(PipeStream stream, int timeoutMs, int maxBytes)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] temp = new byte[4096];
                int totalRead = 0;
                Stopwatch sw = Stopwatch.StartNew();

                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    int remainTime = Math.Max(1, timeoutMs - (int)sw.ElapsedMilliseconds);
                    IAsyncResult ar = null;
                    try
                    {
                        ar = stream.BeginRead(temp, 0, temp.Length, null, null);
                    }
                    catch
                    {
                        return null;
                    }

                    int bytesRead;
                    using (WaitHandle wh = ar.AsyncWaitHandle)
                    {
                        if (!wh.WaitOne(remainTime))
                        {
                            try { stream.Close(); stream.EndRead(ar); } catch { }
                            return null;
                        }
                        // Framework EndRead may wait on this handle: dispose only afterwards.
                        try { bytesRead = stream.EndRead(ar); }
                        catch { return null; }
                    }

                    if (bytesRead <= 0)
                    {
                        break; // EOF
                    }

                    totalRead += bytesRead;
                    if (totalRead > maxBytes)
                    {
                        // 超过 32KiB 限制，拒绝并关闭管道
                        try { stream.Close(); } catch { }
                        return null;
                    }

                    ms.Write(temp, 0, bytesRead);

                    // 检查是否已包含整行结束符 '\n'
                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (temp[i] == (byte)'\n')
                        {
                            return ms.ToArray();
                        }
                    }
                }

                return ms.Length > 0 ? ms.ToArray() : null;
            }
        }

        public bool ProcessSnapshot(IndepenDeskSnapshot snapshot)
        {
            if (snapshot == null) return false;

            lock (_stateLock)
            {
                long nowMs = _timeProvider != null ? _timeProvider() : 0;

                if (string.IsNullOrEmpty(_currentInstance) ||
                    !string.Equals(_currentInstance, snapshot.Instance, StringComparison.OrdinalIgnoreCase))
                {
                    // 发现新实例：接受并重置序列跟踪
                    _currentInstance = snapshot.Instance;
                    _currentSequence = snapshot.Sequence;
                    _freshnessStopwatch.Restart();
                    _lastSnapshotTimeMs = nowMs;
                    _currentSnapshot = snapshot;
                    _lastStaleReported = false;
                    return true;
                }

                // 相同实例：序列必须严格单调递增
                if (snapshot.Sequence > _currentSequence)
                {
                    _currentSequence = snapshot.Sequence;
                    _freshnessStopwatch.Restart();
                    _lastSnapshotTimeMs = nowMs;
                    _currentSnapshot = snapshot;
                    _lastStaleReported = false;
                    return true;
                }

                // 相同实例下的重复或更旧序列：绝对不刷新 freshness!
                return false;
            }
        }

        private void SetConnected(bool connected)
        {
            lock (_stateLock)
            {
                if (_isConnected != connected)
                {
                    _isConnected = connected;
                    OnStateChanged();
                }
            }
        }

        private void OnSnapshotReceived(IndepenDeskSnapshot snapshot)
        {
            EventHandler<SnapshotEventArgs> handler = SnapshotReceived;
            if (handler != null)
            {
                try { handler(this, new SnapshotEventArgs(snapshot)); } catch { }
            }
            OnStateChanged();
        }

        private void OnStateChanged()
        {
            EventHandler handler = StateChanged;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); } catch { }
            }
        }

        public static IndepenDeskSnapshot ParseXmlBytes(byte[] rawBytes, int expectedSessionId, out string error)
        {
            error = null;
            if (rawBytes == null || rawBytes.Length == 0)
            {
                error = "Raw bytes are empty.";
                return null;
            }
            if (rawBytes.Length > 32768)
            {
                error = "Payload exceeds 32768 bytes.";
                return null;
            }

            string xmlContent;
            try
            {
                UTF8Encoding utf8Strict = new UTF8Encoding(false, true);
                xmlContent = utf8Strict.GetString(rawBytes);
            }
            catch (Exception ex)
            {
                error = "Strict UTF-8 decoding error: " + ex.Message;
                return null;
            }

            int nlIdx = xmlContent.IndexOf('\n');
            if (nlIdx >= 0)
            {
                xmlContent = xmlContent.Substring(0, nlIdx);
            }
            xmlContent = xmlContent.Trim();

            return ParseXml(xmlContent, expectedSessionId, out error);
        }

        public static IndepenDeskSnapshot ParseXml(string xml, int expectedSessionId, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(xml))
            {
                error = "XML content is empty.";
                return null;
            }

            // 严格 UTF-8 字节长度 <= 32768
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(xml);
            if (utf8Bytes.Length > 32768)
            {
                error = "XML content exceeds 32KiB boundary.";
                return null;
            }

            try
            {
                XmlReaderSettings settings = new XmlReaderSettings();
                settings.DtdProcessing = DtdProcessing.Prohibit;
                settings.XmlResolver = null;
                settings.MaxCharactersInDocument = 32768;
                settings.IgnoreWhitespace = true;
                settings.IgnoreComments = true;
                settings.IgnoreProcessingInstructions = true;

                using (StringReader sr = new StringReader(xml))
                using (XmlReader reader = XmlReader.Create(sr, settings))
                {
                    if (!reader.Read() || reader.NodeType != XmlNodeType.Element)
                    {
                        error = "No root element found.";
                        return null;
                    }

                    // root depth 必须是 0，名称必须是 exact "state"
                    if (reader.Depth != 0 || !string.Equals(reader.LocalName, "state", StringComparison.Ordinal))
                    {
                        error = "Root element must be exact 'state' at depth 0.";
                        return null;
                    }

                    // 拒绝命名空间
                    if (!string.IsNullOrEmpty(reader.NamespaceURI))
                    {
                        error = "Namespaces are strictly rejected.";
                        return null;
                    }

                    string verStr = reader.GetAttribute("version");
                    string pidStr = reader.GetAttribute("pid");
                    string sessStr = reader.GetAttribute("session");
                    string instStr = reader.GetAttribute("instance");
                    string seqStr = reader.GetAttribute("sequence");

                    int version;
                    if (!int.TryParse(verStr, out version) || version != 1)
                    {
                        error = "Attribute 'version' must be 1.";
                        return null;
                    }

                    int pid;
                    if (!int.TryParse(pidStr, out pid) || pid <= 0)
                    {
                        error = "Attribute 'pid' must be positive.";
                        return null;
                    }

                    int session;
                    if (!int.TryParse(sessStr, out session) || session != expectedSessionId)
                    {
                        error = string.Format("Attribute 'session' mismatch (expected {0}, got {1}).", expectedSessionId, sessStr);
                        return null;
                    }

                    Guid instGuid;
                    if (string.IsNullOrEmpty(instStr) || !Guid.TryParse(instStr, out instGuid) || instGuid == Guid.Empty)
                    {
                        error = "Attribute 'instance' must be a valid non-empty GUID.";
                        return null;
                    }

                    long sequence;
                    if (!long.TryParse(seqStr, out sequence) || sequence <= 0)
                    {
                        error = "Attribute 'sequence' must be a positive long integer.";
                        return null;
                    }

                    List<IndepenDeskMonitorInfo> monitors = new List<IndepenDeskMonitorInfo>();
                    HashSet<string> seenDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    HashSet<int> seenOrdinals = new HashSet<int>();

                    bool isEmptyElement = reader.IsEmptyElement;
                    if (!isEmptyElement)
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.EndElement && string.Equals(reader.LocalName, "state", StringComparison.Ordinal))
                            {
                                break;
                            }

                            if (reader.NodeType == XmlNodeType.Element)
                            {
                                // 直接子元素只能是 monitor，且 depth 必须精确为 1
                                if (reader.Depth != 1 || !string.Equals(reader.LocalName, "monitor", StringComparison.Ordinal))
                                {
                                    error = "Only direct child <monitor> elements allowed at depth 1.";
                                    return null;
                                }

                                if (!string.IsNullOrEmpty(reader.NamespaceURI))
                                {
                                    error = "Child namespace rejected.";
                                    return null;
                                }

                                // monitor 元素必须为空，不允许包含嵌套节点
                                if (!reader.IsEmptyElement)
                                {
                                    error = "<monitor> element must be empty and cannot contain child nodes.";
                                    return null;
                                }

                                if (monitors.Count >= 32)
                                {
                                    error = "Monitors count exceeds 32.";
                                    return null;
                                }

                                string dev = reader.GetAttribute("device");
                                string ordStr = reader.GetAttribute("ordinal");
                                string idxStr = reader.GetAttribute("index");
                                string cntStr = reader.GetAttribute("count");
                                string globStr = reader.GetAttribute("global");

                                if (string.IsNullOrEmpty(dev))
                                {
                                    error = "Monitor device name cannot be empty.";
                                    return null;
                                }

                                if (seenDevices.Contains(dev))
                                {
                                    error = "Duplicate monitor device: " + dev;
                                    return null;
                                }
                                seenDevices.Add(dev);

                                int ord;
                                if (!int.TryParse(ordStr, out ord) || ord < 1 || ord > 32)
                                {
                                    error = "Monitor ordinal must be 1..32.";
                                    return null;
                                }
                                if (seenOrdinals.Contains(ord))
                                {
                                    error = "Duplicate monitor ordinal: " + ord;
                                    return null;
                                }
                                seenOrdinals.Add(ord);

                                int idx;
                                if (!int.TryParse(idxStr, out idx) || idx < 0 || idx > 8)
                                {
                                    error = "Monitor index must be 0..8.";
                                    return null;
                                }

                                int cnt;
                                if (!int.TryParse(cntStr, out cnt) || cnt < 1 || cnt > 9)
                                {
                                    error = "Monitor count must be 1..9.";
                                    return null;
                                }

                                if (idx >= cnt)
                                {
                                    error = string.Format("Monitor index ({0}) must be strictly less than count ({1}).", idx, cnt);
                                    return null;
                                }

                                int global;
                                if (!int.TryParse(globStr, out global) || global < 1 || global > 288)
                                {
                                    error = "Monitor global must be 1..288.";
                                    return null;
                                }

                                monitors.Add(new IndepenDeskMonitorInfo(dev, ord, idx, cnt, global));
                            }
                            else
                            {
                                error = "Unexpected content under <state>: " + reader.NodeType;
                                return null;
                            }
                        }
                    }

                    // 必须读取至 EOF 验证文档完整，不允许尾部垃圾
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Whitespace)
                        {
                            error = "Trailing data after root element: " + reader.NodeType;
                            return null;
                        }
                    }

                    return new IndepenDeskSnapshot(version, pid, session, instGuid.ToString(), sequence, monitors);
                }
            }
            catch (Exception ex)
            {
                error = "XML parse exception: " + ex.Message;
                return null;
            }
        }

        public void Dispose()
        {
            Stop();
            _stopEvent.Dispose();
        }
    }
}
