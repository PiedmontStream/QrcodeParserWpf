using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using ZXing;

namespace QrCodeParser
{
    public partial class MainWindow : Window
    {
        private IHost? AppHost { get; set; }
        private IHttpClientFactory HttpClientFactory { get; init; }

        private static readonly string[] ImageTypes = new[] { "jpg", "png", "gif", "bmp", "tiff", "webp", "svg" };

        private static readonly BackgroundGcScheduler Scheduler = new();

        private static readonly AutoClosedMessageBox CopyMessageBox =
            new("已成功复制到剪贴板！", "提示", TimeSpan.FromSeconds(1));

        public MainWindow()
        {
            // 为HTTPClient准备DI容器
            AppHost = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddHttpClient("AutoFlushClient")
                        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                        {
                            Proxy = HttpClient.DefaultProxy,
                            UseProxy = true
                        });
                })
                .Build();
            AppHost.Start();
            HttpClientFactory = AppHost.Services.GetRequiredService<IHttpClientFactory>();
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Activated += CancelCountdown;
            Deactivated +=  BeginCountdown;
        }

        private static void BeginCountdown(object? o, EventArgs eventArgs)
        {
            Scheduler.StartCountdown();
        }
        private static void CancelCountdown(object? o, EventArgs eventArgs)
        {
            Scheduler.CancelCountdown();
        }


        protected override async void OnClosed(EventArgs e)
        {
            Loaded -= MainWindow_Loaded;
            Activated -= CancelCountdown;
            Deactivated -= BeginCountdown;
            if (AppHost != null)
            {
                await AppHost.StopAsync();
                AppHost.Dispose();
            }
            base.OnClosed(e);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 获取运行此程序时传入的命令行参数
            var args = Environment.GetCommandLineArgs();

            // 如果长度大于 1，说明有外部文件被拖入或者通过命令行传入了参数
            if (args.Length > 1)
            {
                var filePath = args[1]; // 获取拖入的文件路径

                // 检查该路径是否确实是一个存在的文件（防止异常参数）
                if (File.Exists(filePath))
                {

                    // 调用解析方法
                    ProcessQrCodeFile(filePath);

                }
            }
        }

        // 1. 点击中央区域：打开新版 Windows 文件选择器（带路径输入栏）
        private void UploadArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "选择二维码图片",
                Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.tiff,*.svg)|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff;*.svg|所有文件 (*.*)|*.*",
                Multiselect = false
            };

            //带有路径输入/导航栏的 Explorer 控件
            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                ProcessQrCodeFile(filePath);
            }
        }

        // 2. 拖拽文件移入识别区域
        private void UploadArea_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy; // 鼠标变成“复制”图标
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        // 3. 拖拽松开：获取本地文件路径
        private void UploadArea_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                if (files is { Length: > 0 })
                {
                    var filePath = files[0]; // 获取拖入的第一张图片
                    ProcessQrCodeFile(filePath);
                }
            }
        }

        // 4. 解析网络 URL 图片
        private async void AnalyzeUrl_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("请先输入有效的图片 URL", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            //TODO 前台状态切换(锁定下载时的动画等)
            using var fs = new MemoryStream();
            try
            {
                拖拽区域.IsEnabled = false;
                解析按钮.IsEnabled = false;
                try
                {
                    var response = await HttpClientFactory.CreateClient().GetAsync(url);
                    await response.Content.CopyToAsync(fs);
                }
                catch (Exception ex)
                {
                    ResultTextBox.Text = "尝试下载图片失败，请手动下载后拖入解析区域(地址错误或者需要代理才能获取图片？)。\n";
                    // 这玩意没必要添加可配置的网络代理功能了
                    ResultTextBox.Text += ex.Message;
                    return;
                }
                fs.Seek(0, SeekOrigin.Begin);
                ResultTextBox.Text = DecodeQrCodeFromStream(fs);
            }
            finally
            {
                拖拽区域.IsEnabled = true;
                解析按钮.IsEnabled = true;
            }
        }

        // 5. 复制结果到剪贴板
        private void CopyResult_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ResultTextBox.Text))
            {
                Clipboard.SetText(ResultTextBox.Text);
                CopyMessageBox.ShowMessageBox();
            }
        }

        // 核心解析方法存根

        private void ProcessQrCodeFile(string path)
        {
            // 这里可以可选地更新界面上的 PreviewImage 展现图片
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            // ResultTextBox.Text = $"成功读取本地文件：\n{imagePath}\n\n[解析成功示例]: Hello World!";
            ResultTextBox.Text = DecodeQrCodeFromStream(fs);
        }



        private void UrlTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // 方便用户双击或点击时全选 URL
            UrlTextBox.SelectAll();
        }


        [SuppressMessage("ReSharper", "SuggestVarOrType_SimpleTypes")]
        [SuppressMessage("ReSharper", "SuggestVarOrType_BuiltInTypes")]
        private static string DecodeQrCodeFromStream(Stream stream)
        {
            

            try
            {
                var helper = new SvgHelper(stream);
                BitmapImage srcBitmap;
                if (helper.IsSvg)
                {
                    srcBitmap = helper.PngBitmapImage;
                }
                else
                {
                    srcBitmap= new BitmapImage();
                    srcBitmap.BeginInit();
                    srcBitmap.CacheOption = BitmapCacheOption.OnLoad;
                    srcBitmap.StreamSource = stream;
                    srcBitmap.EndInit();
                    srcBitmap.Freeze();
                }

                // 转成 Gray8，方便后续预处理
                FormatConvertedBitmap grayBitmap = new FormatConvertedBitmap();
                grayBitmap.BeginInit();
                grayBitmap.Source = srcBitmap;
                grayBitmap.DestinationFormat = PixelFormats.Gray8;
                grayBitmap.EndInit();
                grayBitmap.Freeze();

                int width = grayBitmap.PixelWidth;
                int height = grayBitmap.PixelHeight;
                int stride = width;

                using var pixelsGuard = ArrayPoolGuard.Rent<byte>(height * stride);
                byte[] pixels = pixelsGuard.Buffer;

                grayBitmap.CopyPixels(
                    pixels,
                    stride,
                    0);
                /*
                 * 艺术二维码预处理
                 * 如果普通二维码很多，可以先尝试关闭
                 */
                BoxBlur(
                    pixels,
                    width,
                    height,
                    radius: 3);


                // 二值化
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = pixels[i] < 140
                        ? (byte)0
                        : (byte)255;
                }


                // ZXing要求RGB格式
                // Gray8需要扩展成BGR32

                using var bgr32Guard = ArrayPoolGuard.Rent<byte>(width * height * 4);
                byte[] bgr32 = bgr32Guard.Buffer;


                for (int i = 0; i < width * height; i++)
                {
                    byte gray = pixels[i];

                    int index = i * 4;

                    bgr32[index] = gray;       // B
                    bgr32[index + 1] = gray;   // G
                    bgr32[index + 2] = gray;   // R
                    bgr32[index + 3] = 255;    // Alpha
                }


                var luminanceSource =
                    new RGBLuminanceSource(
                        bgr32,
                        width,
                        height,
                        RGBLuminanceSource.BitmapFormat.BGR32);

                var reader = new BarcodeReaderGeneric
                {
                    Options =
                    {
                        PossibleFormats = new[] { BarcodeFormat.QR_CODE },
                        TryHarder = true // 开启苦工模式，识别更精准
                    }
                };

                var result = reader.Decode(luminanceSource);
                return result != null ? result.Text : "未检测到二维码";
            }
            catch (NotSupportedException)
            {
                return $"不支持的文件类型或文件已损坏。当前支持文件类型：{string.Join(",", ImageTypes)}";
            }
            catch (Exception ex)
            {
                return $"解析出错: {ex.Message}";
            }
        }

        /// <summary>
        /// 二维码均值处理，对应艺术二维码
        /// </summary>
        /// <param name="pixels"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="radius"></param>
        [SuppressMessage("ReSharper", "SuggestVarOrType_SimpleTypes")]
        [SuppressMessage("ReSharper", "SuggestVarOrType_BuiltInTypes")]
        private static void BoxBlur(IList<byte> pixels, int width, int height, int radius)
        {
            if (radius <= 0)
            {
                return;
            }
            using var guard = ArrayPoolGuard.Rent<byte>(pixels.Count);
            byte[] temp = guard.Buffer;
            // 横向模糊
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sum = 0;
                    int count = 0;

                    for (int k = -radius; k <= radius; k++)
                    {
                        int xx = x + k;

                        if (xx >= 0 && xx < width)
                        {
                            sum += pixels[y * width + xx];
                            count++;
                        }
                    }
                    temp[y * width + x] = (byte)(sum / count);
                }
            }



            // 纵向模糊
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sum = 0;
                    int count = 0;


                    for (int k = -radius; k <= radius; k++)
                    {
                        int yy = y + k;

                        if (yy >= 0 && yy < height)
                        {
                            sum += temp[yy * width + x];
                            count++;
                        }
                    }


                    pixels[y * width + x] = (byte)(sum / count);
                }
            }
        }
        private void Hyperlink_Click(object? sender, RoutedEventArgs e)
        {
            var link = sender as Hyperlink;
            Process.Start(new ProcessStartInfo(link!.NavigateUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
    }
}