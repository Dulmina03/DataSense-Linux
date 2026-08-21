using System;
using System.IO;
using System.Threading.Tasks;
using DataSense.Helpers;
using DataSense.Services;
using Xunit;

namespace DataSense.Tests.Services;

public class SecurityAndPathProtectionTests
{
    [Fact]
    public async Task ProcessExecutionHelper_ArgumentList_DoesNotSubshellInject()
    {
        string maliciousArg = "test; rm -rf /tmp/non_existent_folder_xyz";
        
        var result = await ProcessExecutionHelper.ExecuteAsync("echo", new[] { maliciousArg }, timeoutMs: 1000);

        Assert.True(result.Success);
        Assert.Equal(maliciousArg, result.StandardOutput);
    }

    [Fact]
    public void StorageService_SanitizePath_StripsPathTraversal()
    {
        var service = new LinuxStorageService();
        string inputPath = "/home/user/DataSense/../../../etc/passwd";
        string sanitized = service.SanitizePath(inputPath);

        Assert.DoesNotContain("..", sanitized);
    }
}
