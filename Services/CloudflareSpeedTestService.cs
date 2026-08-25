using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DataSense.Services;

public class CloudflareSpeedTestService : ISpeedTestService
{
    private readonly HttpClient _httpClient;
    private const double TargetPhaseDurationSeconds = 8.5; // ~8.5 seconds sustained high-resolution measurement

    public CloudflareSpeedTestService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<double> TestPingAsync(CancellationToken cancellationToken)
    {
        // Ping measurement using Cloudflare CDN /cdn-cgi/trace
        try
        {
            var sw = Stopwatch.StartNew();
            using var response = await _httpClient.GetAsync("https://speed.cloudflare.com/cdn-cgi/trace", cancellationToken);
            sw.Stop();
            
            return sw.Elapsed.TotalMilliseconds;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<double> TestDownloadAsync(Action<double> progressCallback, CancellationToken cancellationToken)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            long totalBytesRead = 0;
            var buffer = new byte[65536];

            // Sustained multi-second streaming download
            while (sw.Elapsed.TotalSeconds < TargetPhaseDurationSeconds && !cancellationToken.IsCancellationRequested)
            {
                var url = "https://speed.cloudflare.com/__down?bytes=25000000";
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    totalBytesRead += bytesRead;

                    if (sw.Elapsed.TotalSeconds > 0.08)
                    {
                        double currentSpeedMbps = (totalBytesRead * 8.0 / 1_000_000.0) / sw.Elapsed.TotalSeconds;
                        progressCallback(currentSpeedMbps);
                    }

                    if (sw.Elapsed.TotalSeconds >= TargetPhaseDurationSeconds)
                    {
                        break;
                    }
                }
            }

            sw.Stop();
            double finalSpeedMbps = (totalBytesRead * 8.0 / 1_000_000.0) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
            progressCallback(finalSpeedMbps);

            return finalSpeedMbps;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<double> TestUploadAsync(Action<double> progressCallback, CancellationToken cancellationToken)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            long totalBytesSent = 0;
            int sliceSize = 8_000_000;
            var payload = new byte[sliceSize];
            new Random().NextBytes(payload);

            // Sustained multi-second streaming upload
            while (sw.Elapsed.TotalSeconds < TargetPhaseDurationSeconds && !cancellationToken.IsCancellationRequested)
            {
                long baseSent = totalBytesSent;
                var content = new ProgressUploadContent(payload, currentSliceBytes =>
                {
                    long overallSent = baseSent + currentSliceBytes;
                    if (sw.Elapsed.TotalSeconds > 0.08)
                    {
                        double currentSpeedMbps = (overallSent * 8.0 / 1_000_000.0) / sw.Elapsed.TotalSeconds;
                        progressCallback(currentSpeedMbps);
                    }
                }, chunkSize: 32768);

                using var response = await _httpClient.PostAsync("https://speed.cloudflare.com/__up", content, cancellationToken);
                totalBytesSent += sliceSize;

                if (sw.Elapsed.TotalSeconds >= TargetPhaseDurationSeconds)
                {
                    break;
                }
            }

            sw.Stop();
            double finalSpeedMbps = (totalBytesSent * 8.0 / 1_000_000.0) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
            progressCallback(finalSpeedMbps);

            return finalSpeedMbps;
        }
        catch
        {
            return 0;
        }
    }

    private sealed class ProgressUploadContent : HttpContent
    {
        private readonly byte[] _data;
        private readonly Action<long> _sliceProgressCallback;
        private readonly int _chunkSize;

        public ProgressUploadContent(byte[] data, Action<long> sliceProgressCallback, int chunkSize = 32768)
        {
            _data = data;
            _sliceProgressCallback = sliceProgressCallback;
            _chunkSize = chunkSize;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            long sentInThisSlice = 0;
            int offset = 0;

            while (offset < _data.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int toSend = Math.Min(_chunkSize, _data.Length - offset);
                await stream.WriteAsync(_data.AsMemory(offset, toSend), cancellationToken);
                await stream.FlushAsync(cancellationToken);

                offset += toSend;
                sentInThisSlice += toSend;

                _sliceProgressCallback(sentInThisSlice);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _data.Length;
            return true;
        }
    }
}
