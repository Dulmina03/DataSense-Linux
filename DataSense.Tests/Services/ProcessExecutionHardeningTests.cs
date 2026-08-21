using System;
using System.Threading.Tasks;
using DataSense.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class ProcessExecutionHardeningTests
{
    [Fact]
    public async Task ProcessExecutionHelper_ExecutesBasicCommand_CapturesOutput()
    {
        var result = await ProcessExecutionHelper.ExecuteAsync("echo", new[] { "hello", "world" }, timeoutMs: 2000);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello world", result.StandardOutput);
    }

    [Fact]
    public async Task ProcessExecutionHelper_NonExistentExecutable_FailsGracefully()
    {
        var result = await ProcessExecutionHelper.ExecuteAsync("non_existent_binary_xyz_999", timeoutMs: 1000);

        Assert.False(result.Success);
        Assert.NotNull(result.StandardError);
    }

    [Fact]
    public async Task ProcessExecutionHelper_TimesOutBoundedExecution()
    {
        // Execute sleep 10 with a short 200ms timeout
        var result = await ProcessExecutionHelper.ExecuteAsync("sleep", new[] { "10" }, timeoutMs: 200);

        Assert.False(result.Success);
        Assert.True(result.TimedOut);
    }
}
