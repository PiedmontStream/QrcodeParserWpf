using System.Runtime.InteropServices;
using System.Windows;

namespace QrCodeParser;

public static partial class ProcessManager
{
    // ReSharper disable once IdentifierTypo
    // ReSharper disable once InconsistentNaming
    private const uint ASFW_ANY = uint.MaxValue;
    [LibraryImport("user32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint dwProcessId);

    private const string MutexName = @"Local\my-qrcode-tool-mutex";
    private const string EventName = @"Local\my-qrcode-tool-event";

    private static Mutex? _mutex;
    private static EventWaitHandle? _eventWaitHandle;
    private static RegisteredWaitHandle? _registeredWait;
    private static bool _hasLock;

    public static void GetProcessLock(out bool holdsLock)
    {
        _mutex = new Mutex(true, MutexName, out _hasLock);

        if (!_hasLock)
        {
            // 没有抢到锁，向旧实例发送唤醒信号
            NotifyFirstInstance();
            holdsLock = false;
            return;
        }

        // 抢锁成功，创建事件句柄监听后续唤醒信号
        _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

        // 使用 ThreadPool 后台线程池监听信号
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _eventWaitHandle,
            OnSignalReceived,
            null,
            -1,
            false
        );

        // 绑定进程退出时的清理逻辑
        AppDomain.CurrentDomain.ProcessExit += (s, e) => ReleaseLock();

        holdsLock = true;
    }

    /// <summary>
    /// 第二个实例调用：发送唤醒信号给第一个实例
    /// </summary>
    private static void NotifyFirstInstance()
    {
        try
        {
            AllowSetForegroundWindow(ASFW_ANY); //将前台的许可送出去
            using var evt = EventWaitHandle.OpenExisting(EventName);
            evt.Set(); // 触发信号
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 事件尚未创建的极端边缘情况处理
        }
    }

    /// <summary>
    /// 第一个实例接收到信号后的回调
    /// </summary>
    private static void OnSignalReceived(object? state, bool timedOut)
    {
        // 切换回 UI 主线程激活主窗口
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
                AllowSetForegroundWindow(ASFW_ANY);
                mainWindow.Show();
                mainWindow.Activate();

                // 强制置顶并取消，突破 Windows 前台抢占限制
                mainWindow.Topmost = true;
                mainWindow.Topmost = false;
            }
        });
    }

    public static void ReleaseLock()
    {
        _registeredWait?.Unregister(null);

        if (_eventWaitHandle != null)
        {
            _eventWaitHandle.Dispose();
            _eventWaitHandle = null;
        }

        if (_mutex != null && _hasLock)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException) { }
            finally
            {
                _mutex.Dispose();
                _mutex = null;
                _hasLock = false;
            }
        }
    }
}