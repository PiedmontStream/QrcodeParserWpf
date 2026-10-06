using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace QrCodeParser
{
    /// <summary>
    /// 创建一个可以自动关闭的确认框
    /// </summary>
    internal sealed partial class AutoClosedMessageBox : IDisposable
    {
        private readonly TimeSpan _delay;
        private readonly Timer _timeoutTimer;
        private readonly string _caption;
        private readonly string _text;

        public AutoClosedMessageBox(string text, string caption, TimeSpan delayFromClose)
        {
            _caption = caption;
            _text = text;
            _delay = delayFromClose;
            _timeoutTimer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        }


        public void ShowMessageBox()
        {
            _timeoutTimer.Change(_delay, Timeout.InfiniteTimeSpan); // 自动关闭
            MessageBox.Show(_text, _caption, MessageBoxButton.OK, MessageBoxImage.Information);
        }


        void OnTimerElapsed(object? state)
        {
            IntPtr mbWnd = FindWindow("#32770", _caption); // lpClassName is #32770 for MessageBox
            if (mbWnd != IntPtr.Zero)
                SendMessage(mbWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            _timeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        public void Dispose()
        {
            _timeoutTimer.Dispose();
        }

        // ReSharper disable InconsistentNaming
        private const int WM_CLOSE = 0x0010;
        [LibraryImport("user32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16, EntryPoint = "FindWindowW")]
        private static partial IntPtr FindWindow(string lpClassName, string lpWindowName);

        [LibraryImport("user32", EntryPoint = "SendMessageW")]
        private static partial IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    }
}
