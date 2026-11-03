using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NTEServerSwitch.Core
{
    /// <summary>
    /// HTTP 拉取 + 重试。只对"可能是网络抖动"的错误重试，404 之类的直接失败。
    /// </summary>
    public sealed class UpdateClient : IDisposable
    {
        public const int MaxAttempts = 3;

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

        private readonly HttpClient _http;
        private readonly LogService _log = LogService.Instance;

        public UpdateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _http = new HttpClient(handler) { Timeout = RequestTimeout };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("NTEServerSwitch/1.1");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        }

        public Task<string> GetStringAsync(string url, CancellationToken token)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("地址为空", "url");
            }

            return ExecuteAsync(async delegate
            {
                using (HttpResponseMessage response =
                       await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                }
            }, "获取 " + url, token);
        }

        public async Task<long> DownloadAsync(string url, string destinationPath, IProgress<long> progress,
                                              CancellationToken token)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("地址为空", "url");
            }

            Exception last = null;
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    string directory = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeout.CancelAfter(DownloadTimeout);
                        using (HttpResponseMessage response =
                               await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                                         .ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();
                            using (Stream input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
                            using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
                                                              FileShare.None, 64 * 1024, true))
                            {
                                var buffer = new byte[64 * 1024];
                                long received = 0;
                                while (true)
                                {
                                    int read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token)
                                                         .ConfigureAwait(false);
                                    if (read <= 0)
                                    {
                                        break;
                                    }
                                    await output.WriteAsync(buffer, 0, read, timeout.Token).ConfigureAwait(false);
                                    received += read;
                                    if (progress != null)
                                    {
                                        progress.Report(received);
                                    }
                                }
                                return received;
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    last = new TimeoutException("下载超时");
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (!IsTransient(ex) || attempt == MaxAttempts)
                    {
                        break;
                    }
                }

                if (attempt < MaxAttempts)
                {
                    string reason = last == null ? "未知错误" : last.Message;
                    _log.Warn("下载失败（第 " + attempt + " 次）：" + reason + "，稍后重试…");
                    await Task.Delay(TimeSpan.FromMilliseconds(700 * attempt), token).ConfigureAwait(false);
                }
            }

            throw new IOException("下载失败：" + url + "（" + (last == null ? "未知错误" : last.Message) + "）", last);
        }

        private async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string description, CancellationToken token)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    last = new TimeoutException("请求超时");
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (!IsTransient(ex) || attempt == MaxAttempts)
                    {
                        break;
                    }
                }

                if (attempt < MaxAttempts)
                {
                    string reason = last == null ? "未知错误" : last.Message;
                    _log.Warn(description + " 失败（第 " + attempt + " 次）：" + reason + "，稍后重试…");
                    await Task.Delay(TimeSpan.FromMilliseconds(700 * attempt), token).ConfigureAwait(false);
                }
            }

            throw new IOException(description + " 失败：" + (last == null ? "未知错误" : last.Message), last);
        }

        private static bool IsTransient(Exception exception)
        {
            if (exception == null)
            {
                return false;
            }

            var requestException = exception as HttpRequestException;
            if (requestException != null && requestException.StatusCode.HasValue)
            {
                int code = (int)requestException.StatusCode.Value;
                return code >= 500 || code == 408 || code == 429;
            }

            return exception is HttpRequestException
                   || exception is IOException
                   || exception is TimeoutException;
        }

        public void Dispose()
        {
            try
            {
                _http.Dispose();
            }
            catch
            {
                // 忽略。
            }
        }
    }
}
