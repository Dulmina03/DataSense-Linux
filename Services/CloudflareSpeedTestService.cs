using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DataSense.Services;

public class CloudflareSpeedTestService : ISpeedTestService
{
    private readonly HttpClient _httpClient;

    public CloudflareSpeedTestService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
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
            // Download a 25MB payload
            var url = "https://speed.cloudflare.com/__down?bytes=25000000";
            
            var sw = Stopwatch.StartNew();
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[81920];
            long totalBytesRead = 0;
            int bytesRead;
            
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                totalBytesRead += bytesRead;
                
                // Report intermediate speed (Mbps)
                if (sw.Elapsed.TotalSeconds > 0.1)
                {
                    double currentSpeedMbps = (totalBytesRead * 8.0 / 1_000_000.0) / sw.Elapsed.TotalSeconds;
                    progressCallback(currentSpeedMbps);
                }
            }
            sw.Stop();
            
            return (totalBytesRead * 8.0 / 1_000_000.0) / sw.Elapsed.TotalSeconds;
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
            // Upload a 10MB payload
            var url = "https://speed.cloudflare.com/__up";
            int payloadSize = 10000000;
            var payload = new byte[payloadSize];
            new Random().NextBytes(payload);
            
            var content = new ByteArrayContent(payload);
            
            var sw = Stopwatch.StartNew();
            using var response = await _httpClient.PostAsync(url, content, cancellationToken);
            sw.Stop();
            
            double finalSpeedMbps = (payloadSize * 8.0 / 1_000_000.0) / sw.Elapsed.TotalSeconds;
            progressCallback(finalSpeedMbps);
            
            return finalSpeedMbps;
        }
        catch
        {
            return 0;
        }
    }
}
