using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml.Media.Imaging;

namespace InkDrawGen.Helpers
{
    internal static class PngExportService
    {
        internal static WriteableBitmap BuildDummy(int width, int height, bool transparent)
        {
            var bmp = new WriteableBitmap(width, height);
            using (var stream = bmp.PixelBuffer.AsStream())
            {
                var stride = width * 4;
                var buf = new byte[stride * height];

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var i = y * stride + x * 4;
                        var isCheck = (((x >> 4) ^ (y >> 4)) & 1) == 0;

                        byte a;
                        byte r;
                        byte g;
                        byte b;

                        if (transparent)
                        {
                            a = 0;
                            r = g = b = 0;
                        }
                        else
                        {
                            a = 255;
                            r = g = b = 255;
                        }

                        if (isCheck)
                        {
                            if (transparent)
                            {
                                a = 255;
                                r = 230;
                                g = 230;
                                b = 230;
                            }
                            else
                            {
                                r = 235;
                                g = 235;
                                b = 235;
                            }
                        }
                        else
                        {
                            if (!transparent)
                            {
                                r = 200;
                                g = 200;
                                b = 200;
                            }
                        }

                        // crosshair
                        if (x == width / 2 || y == height / 2)
                        {
                            a = 255;
                            r = 255;
                            g = 0;
                            b = 0;
                        }

                        // BGRA
                        buf[i + 0] = b;
                        buf[i + 1] = g;
                        buf[i + 2] = r;
                        buf[i + 3] = a;
                    }
                }

                stream.Write(buf, 0, buf.Length);
            }

            bmp.Invalidate();
            return bmp;
        }

        internal static async Task SaveAsync(WriteableBitmap bmp, string filePath)
        {
            var folderPath = Path.GetDirectoryName(filePath) ?? string.Empty;
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("filePath", nameof(filePath));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("filePath", nameof(filePath));

            StorageFolder storageFolder;
            try
            {
                storageFolder = await StorageFolder.GetFolderFromPathAsync(folderPath);
            }
            catch (DirectoryNotFoundException)
            {
                // フォルダが存在しない場合は階層を作成する
                var parts = folderPath.Split(Path.DirectorySeparatorChar);
                if (parts.Length == 0) throw;

                // drive root (e.g. "C:")
                var currentPath = parts[0] + Path.DirectorySeparatorChar;
                storageFolder = await StorageFolder.GetFolderFromPathAsync(currentPath);
                for (var i = 1; i < parts.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(parts[i])) continue;
                    storageFolder = await storageFolder.CreateFolderAsync(parts[i], CreationCollisionOption.OpenIfExists);
                }
            }
            catch
            {
                storageFolder = null;
            }

            var file = await storageFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await SaveAsync(bmp, file);
        }

        internal static async Task SaveAsync(WriteableBitmap bmp, StorageFile file)
        {
            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);

                var pixels = bmp.PixelBuffer.ToArray();
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)bmp.PixelWidth, (uint)bmp.PixelHeight, 96, 96, pixels);

                await encoder.FlushAsync();
            }
        }
    }
}
