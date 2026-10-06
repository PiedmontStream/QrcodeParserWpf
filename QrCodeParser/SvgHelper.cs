using Svg;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using System.Xml;


namespace QrCodeParser
{
    internal sealed class SvgHelper
    {
        public bool IsSvg { get; init; }

        public BitmapImage PngBitmapImage { get; init; }
        public SvgHelper(Stream stream)
        {
            if (!stream.CanSeek)
            {
                throw new ArgumentException("该方法需要的流必须支持 Seek 操作。", nameof(stream));
            }
            IsSvg = IsSvgStream(stream);
            PngBitmapImage = IsSvg ? SvgToBitmapImage(stream) : new BitmapImage();
        }


        private static bool IsSvgStream(Stream stream)
        {
            Debug.Assert(stream is not null);
            if (!stream.CanRead) return false;

            // 记录原始位置，判定完后复原流指针
            var originalPosition = stream.Position;

            try
            {
                // 1. 只读取前 2048 字节进行快速检查（覆盖 BOM、XML 声明、DOCTYPE 和空白）
                byte[] buffer = new byte[2048];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) return false;

                // 2. 使用 StreamReader 处理编码和 UTF-8 BOM
                using (var ms = new MemoryStream(buffer, 0, bytesRead))
                using (var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    string headerText = reader.ReadToEnd();

                    // 3. 快速文本预检：去除开头空白后，查看是否包含 <svg 标签
                    if (!headerText.Contains("<svg", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                // 4. 精确确认：使用 XmlReader 进行极轻量流式解析，只读取根节点
                stream.Position = originalPosition;

                var xmlSettings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore, // 忽略外部 DTD 防止 XML 实体攻击(XXE)
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                };

                using (var xmlReader = XmlReader.Create(stream, xmlSettings))
                {
                    // 移动到第一个元素节点（即根节点）
                    while (xmlReader.Read())
                    {
                        if (xmlReader.NodeType == XmlNodeType.Element)
                        {
                            // 判断根节点名称是否为 "svg" (不区分大小写)
                            return string.Equals(xmlReader.LocalName, "svg", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }

                return false;
            }
            catch
            {
                // 如果 XML 格式损坏或无法解析，说明不是合法的 SVG
                return false;
            }
            finally
            {
                // 重置流指针，不影响后续对该 Stream 的读取
                stream.Position = originalPosition;
            }
        }

        private static BitmapImage SvgToBitmapImage(Stream svgStream)
        {
            var position = svgStream.Position;

            // 2. 解构并渲染 SVG 垫上白底
            var svgDocument = SvgDocument.Open<SvgDocument>(svgStream);
            using var svgBitmap = svgDocument.Draw();

            using var finalBitmap = new Bitmap(svgBitmap.Width, svgBitmap.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(finalBitmap))
            {
                g.Clear(Color.White); // 添加白背景
                g.DrawImage(svgBitmap, 0, 0, svgBitmap.Width, svgBitmap.Height);
            }

            // 3. 将 GDI+ Bitmap 写入临时流并构建 WPF 的 BitmapImage
            using (var tempMs = new MemoryStream())
            {
                finalBitmap.Save(tempMs, ImageFormat.Png);
                tempMs.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad; // 立即完全加载到内存
                bitmapImage.StreamSource = tempMs;
                bitmapImage.EndInit();

                bitmapImage.Freeze();
                svgStream.Position = position;
                return bitmapImage;
            }
        }



    }
}
