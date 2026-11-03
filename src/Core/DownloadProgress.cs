namespace NTEServerSwitch.Core
{
    /// <summary>单个文件下载进度（含瞬时速度，由 UpdateClient 按固定间隔平滑估算）。</summary>
    public sealed class DownloadProgress
    {
        /// <summary>已接收字节数。</summary>
        public long Received { get; set; }

        /// <summary>总字节数；服务器没给 Content-Length 时为 -1。</summary>
        public long Total { get; set; }

        /// <summary>估算速度（字节/秒）。刚开始采样时可能为 0。</summary>
        public double BytesPerSecond { get; set; }
    }
}
