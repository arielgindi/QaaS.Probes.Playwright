using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// The processes the recorder started (the SDK's builds and its compiler), killed when the recording ends, with their
/// trees, as they would otherwise outlive it: on Windows they have no console of the recorder's, so Ctrl+C never reaches
/// them. On Windows each one with its tree is also put in a job that closes with the recorder, so they end even when the
/// recorder is killed. A process that must outlive the recorder's own end, as the compiler's server others share, is
/// owned <c>alone</c>: killed without its tree, and kept out of the job.
/// </summary>
internal sealed class ChildProcesses
{
    private readonly Lock _gate = new();
    private readonly Dictionary<int, (Process Process, bool Alone)> _owned = [];
    private readonly Lazy<SafeHandle?> _job = new(() => OperatingSystem.IsWindows() ? WindowsJob.Create() : null);

    /// <summary>The processes owned now, by id.</summary>
    public IReadOnlyCollection<int> Ids
    {
        get { lock (_gate) return [.. _owned.Keys]; }
    }

    /// <summary>Owns <paramref name="process"/> until the result is disposed, as it ends by itself.</summary>
    public IDisposable Own(Process process, bool alone = false)
    {
        int id;
        try
        {
            id = process.Id;
        }
        catch (InvalidOperationException)
        {
            return new Release(this, -1); // It never started.
        }

        lock (_gate) _owned[id] = (process, alone);
        if (!alone && _job.Value is { } job) WindowsJob.Assign(job, process);
        return new Release(this, id);
    }

    /// <summary>Kills every process owned, with its tree unless it is owned alone, and forgets them.</summary>
    public void KillAll()
    {
        List<(Process Process, bool Alone)> owned;
        lock (_gate)
        {
            owned = [.. _owned.Values];
            _owned.Clear();
        }

        foreach (var (process, alone) in owned)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: !alone);
            }
            catch (Exception gone) when (gone is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // It ended meanwhile, or is not ours to kill any more.
            }
        }
    }

    private sealed class Release(ChildProcesses owner, int id) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate) owner._owned.Remove(id);
        }
    }

    // A job object that kills its processes when its last handle closes, as the recorder's does when it ends however it
    // ends. A process it cannot take (in a job that forbids it) is still killed by KillAll.
    private static class WindowsJob
    {
        private const int ExtendedLimitInformation = 9;
        private const uint KillOnJobClose = 0x2000;

        public static SafeHandle? Create()
        {
            var job = CreateJobObjectW(IntPtr.Zero, null);
            if (job.IsInvalid) return null;
            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
            var size = Marshal.SizeOf<ExtendedLimits>();
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, buffer, false);
                if (SetInformationJobObject(job, ExtendedLimitInformation, buffer, (uint)size)) return job;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            job.Dispose();
            return null;
        }

        public static void Assign(SafeHandle job, Process process)
        {
            try
            {
                AssignProcessToJobObject(job, process.Handle);
            }
            catch (Exception gone) when (gone is InvalidOperationException or Win32Exception)
            {
                // It ended already.
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeJobHandle CreateJobObjectW(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(SafeHandle job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(SafeHandle job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        private sealed class SafeJobHandle() : SafeHandle(IntPtr.Zero, ownsHandle: true)
        {
            public override bool IsInvalid => handle == IntPtr.Zero;

            protected override bool ReleaseHandle() => CloseHandle(handle);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
