using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AppWrap.Build;

public sealed record ProcessResult(int ExitCode, string Log);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout ?? TimeSpan.FromMinutes(10));
        using var job = ProcessJob.Create();
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) {
            WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true } };
        foreach (var arg in arguments) process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start()) throw new IOException("The packaging engine could not start.");
        try
        {
            // Process.Start cannot create a suspended child. Only trusted bundled engines are
            // launched here: a child they create before this immediate assignment can escape
            // the job. Assignment failure is fatal; this is containment, not a security sandbox.
            job.Assign(process.SafeHandle);
        }
        catch
        {
            KillRoot(process);
            await WaitBounded(process.WaitForExitAsync()).ConfigureAwait(false);
            throw;
        }
        process.StandardInput.Close();
        var stdout = Drain(process.StandardOutput, limit.Token);
        var stderr = Drain(process.StandardError, limit.Token);
        // The same deadline covers root exit AND pipe EOF. A descendant can inherit an
        // output handle and outlive its parent; waiting only for the root is insufficient.
        var completion = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
        try
        {
            await completion.WaitAsync(limit.Token).ConfigureAwait(false);
            return new(process.ExitCode, stdout.Result + "\n" + stderr.Result);
        }
        catch
        {
            bool timedOut = limit.IsCancellationRequested;
            job.Stop();
            KillRoot(process);
            limit.Cancel();
            // Closing the owned read ends also bounds cleanup if a pipe owner escaped in
            // the startup race. Never wait indefinitely for a foreign pipe writer.
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            await WaitBounded(completion).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            if (timedOut) throw new TimeoutException("Packaging took too long. Try a smaller application folder.");
            throw;
        }
    }

    private static void KillRoot(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or NotSupportedException) { }
    }

    private static async Task WaitBounded(Task completion)
    {
        try { await completion.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException or Win32Exception)
        {
            // Observe any later task fault without retaining a blocking cleanup wait.
            _ = completion.ContinueWith(static task => _ = task.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static async Task<string> Drain(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder();
        bool truncated = false;
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > 131072) truncated = true;
            result.Append(buffer, 0, count);
            if (result.Length > 131072) result.Remove(0, result.Length - 131072);
        }
        return (truncated ? "[earlier log output truncated]\n" : "") + result;
    }

    private sealed class ProcessJob : SafeHandleZeroOrMinusOneIsInvalid
    {
        private ProcessJob() : base(ownsHandle: true) { }

        internal static ProcessJob Create()
        {
            var job = CreateJobObjectW(IntPtr.Zero, null);
            if (job.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError(); job.Dispose();
                throw new Win32Exception(error, "Could not create packaging process containment.");
            }
            var limits = new ExtendedLimitInformation { BasicLimitInformation = new BasicLimitInformation { LimitFlags = 0x2000 } };
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
            {
                var error = Marshal.GetLastPInvokeError(); job.Dispose();
                throw new Win32Exception(error, "Could not configure packaging process containment.");
            }
            return job;
        }

        internal void Assign(SafeProcessHandle process)
        {
            if (!AssignProcessToJobObject(this, process))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not contain the packaging process. Close AppWrap and try again.");
        }
        internal void Stop() => TerminateJobObject(this, 1);
        protected override bool ReleaseHandle() => CloseHandle(handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation
        {
            public BasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ProcessJob CreateJobObjectW(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(ProcessJob job, int informationClass, ref ExtendedLimitInformation information, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(ProcessJob job, SafeProcessHandle process);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(ProcessJob job, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
