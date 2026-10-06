using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;


namespace QrCodeParser;

public sealed partial class BackgroundGcScheduler : IDisposable
{
    private readonly Timer _gcTimer;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(3);

    public BackgroundGcScheduler()
    {
        // 初始化定时器，传入 Timeout.Infinite 表示初始状态为停止
        _gcTimer = new Timer(OnGcTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 当应用进入后台/非激活/最小化时调用
    /// </summary>
    public void StartCountdown()
    {
        // 开启 30 秒单次倒计时（Period 传 Timeout.InfiniteTimeSpan 表示不重复执行）
        _gcTimer.Change(_delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// 当应用恢复前台/激活时调用
    /// </summary>
    public void CancelCountdown()
    {
        // 传入 Timeout.Infinite 停止定时器，即取消事件
        _gcTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void OnGcTimerCallback(object? state)
    {
        // 定时器触发，在线程池线程执行 GC
        MemoryManager.TrimMemoryToOS();
    }

    public void Dispose()
    {
        _gcTimer?.Dispose();
    }

    private static partial class MemoryManager
    {
        // Windows API: 强制清空进程工作集（Physical Working Set）
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);


        // ReSharper disable once InconsistentNaming
        public static void TrimMemoryToOS()
        {
            // 1. 设置大对象堆（LOH）在下一次 GC 时进行压缩（避免 LOH 碎片占用内存页）
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

            // 2. 触发 2 代 GC，指定 Aggressive（激进模式）、阻塞式且强制压缩
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            // 3. 等待终结器队列（Finalizers）处理完毕
            GC.WaitForPendingFinalizers();

            // 4. 再次触发 GC，清理终结器释放出的对象
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            // 5. 提示 GC 释放不再使用的段，并向操作系统归还物理内存
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // 在 Windows 上，将 Working Set 缩减到最小，强制 OS 将未占用的页面回收或移入 Standby List
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
            }
        }
    }
}

