namespace ImagingTools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public class ImageOptimizer
    {
        /// <summary>
        /// Where to save the temporary images
        /// </summary>
        public string WorkingFolder { get; set; }

        /// <summary>
        /// Path to the optipng.exe.
        /// </summary>
        public string OptipngPath { get; set; }

        /// <summary>
        /// For Jpeg: Since jpeg is not lossless, this is the minimum similarity threshold allowed.
        /// </summary>
        public double MinimumSimilarity { get; set; }

        /// <summary>
        /// For Jpeg: Since comparing a big image is time consuming, images can be resized to fit into this width so that the comparisons are faster.
        /// </summary>
        public int ComparisonWidth { get; set; }

        /// <summary>
        /// For Jpeg: When compressing a file and the optimized byte count is lower than this parameter, operation is cancelled. i.e.: "Not worth it"
        /// </summary>
        public int MinCompressionBytes { get; set; }

        /// <summary>
        /// For Jpeg: List of quality percentages to test. Default are 65, 75, 80, 85, 90
        /// </summary>
        public List<int> JpegQualities { get; set; }

        public ImageOptimizer()
        {
        }

        public ImageOptimizer(
            string optipngPath,
            string workingFolder,
            double minJpegSimilarity = 95,
            int qualityCheckWidth = 500,
            int meaningfulFileSizeChange = 2048,
            List<int> jpegQualities = null)
        {
            OptipngPath = optipngPath;
            WorkingFolder = workingFolder;
            MinimumSimilarity = minJpegSimilarity;
            ComparisonWidth = qualityCheckWidth;
            MinCompressionBytes = meaningfulFileSizeChange;
            JpegQualities = jpegQualities;

            if (JpegQualities == null || JpegQualities.Count == 0)
            {
                JpegQualities = new List<int> { 65, 75, 80, 85, 90 };
            }
            else
            {
                JpegQualities.Sort();
            }
        }

        public void CompressImagesInFolder(bool onlyTopDirectory = true,
            bool overrideFiles = true,
            bool deleteUnoptimizable = false)
        {
            var inputFiles = Directory.GetFiles(WorkingFolder,
                "*.*",
                onlyTopDirectory ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories)
                .ToList();

            if (deleteUnoptimizable)
            {
                foreach (var nonImage in inputFiles.Where(path => IsFileOptimizable(path) == false))
                {
                    File.Delete(nonImage);
                }
            }

            inputFiles.RemoveAll(path => IsFileOptimizable(path) == false);

            double totalBytes = 0, totalOptimized = 0;
            int ok = 0, current = 0;
            Console.WriteLine($"Optimizing {inputFiles.Count} files:");

            var options = new ParallelOptions()
            {
                MaxDegreeOfParallelism = 8
            };

            var tryLater = new List<Tuple<string, string>>();

            Parallel.For(0, inputFiles.Count, options, i =>
            {
                Interlocked.Increment(ref current);
                var sourcePath = inputFiles[i];
                var info = new FileInfo(sourcePath);
                var newPath = $"{sourcePath}.optimized";

                var attempt = OptimizeFile(sourcePath, newPath);

                if (!attempt.Success && File.Exists(newPath))
                {
                    attempt.Success = true;
                }

                if (attempt.Success)
                {
                    Console.WriteLine($"\t{current})\t{info.Name,-60} OK: {attempt.Message}");

                    Interlocked.Increment(ref ok);
                    totalOptimized += new FileInfo(newPath).Length;
                    totalBytes += info.Length;

                    if (overrideFiles)
                    {
                        File.Delete(sourcePath);

                        try
                        {
                            File.Move(newPath, sourcePath);
                        }
                        catch
                        {
                            tryLater.Add(Tuple.Create(newPath, sourcePath));
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"\t{current})\t{info.Name,-60} {attempt.Message}");

                    if (deleteUnoptimizable)
                    {
                        File.Delete(sourcePath);
                    }
                }
            });

            var saved = totalBytes - totalOptimized;
            var ratio = totalBytes > 0 ? 100 * saved / totalBytes : 0;
            Console.WriteLine($"Optimized {ok} of {current} / {inputFiles.Count}. Save rate: {ratio.ToString("N1")}%. Total KB {(totalBytes / 1024).ToString("N0")}, new size {(totalOptimized / 1024).ToString("N0")}, saved {(saved / 1024).ToString("N0")}");

            inputFiles = Directory.GetFiles(WorkingFolder,
                "*.optimized",
                onlyTopDirectory ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories)
                .ToList();

            foreach (var path in inputFiles)
            {
                tryLater.Add(Tuple.Create(path, path.Replace(".optimized", "")));
            }

            foreach (var retry in tryLater)
            {
                try
                {
                    if (!File.Exists(retry.Item1))
                    {
                        continue;
                    }

                    if (File.Exists(retry.Item2))
                    {
                        File.Delete(retry.Item2);
                    }

                    File.Move(retry.Item1, retry.Item2);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR! {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        private static bool IsFileOptimizable(string sourcePath)
        {
            var info = new FileInfo(sourcePath);

            switch (info.Extension.ToLower())
            {
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".bmp":
                case ".gif":
                case ".pnm":
                case ".tiff":
                    return true;

                default:
                    return false;
            }
        }

        public Result OptimizeFile(string sourcePath, string targetPath)
        {
            var info = new FileInfo(sourcePath);

            switch (info.Extension.ToLower())
            {
                case ".jpg":
                case ".jpeg":
                    return CompressJpeg(sourcePath, targetPath);

                case ".png":
                case ".bmp":
                case ".gif":
                case ".pnm":
                case ".tiff":
                    return CompressPng(sourcePath, targetPath);

                default:
                    return Result.NotOk($"File with an unoptimizable extension: {info.Name}");
            }
        }

        public Result CompressJpeg(string sourcePath, string targetPath)
        {
            Image sourceImage = null;

            try
            {
                sourceImage = Image.FromFile(sourcePath);
                var sourceInfo = new FileInfo(sourcePath);

                foreach (var quality in this.JpegQualities)
                {
                    CloneJpeg(sourceImage, targetPath, quality);

                    var newSize = new FileInfo(targetPath).Length;

                    if (newSize >= sourceInfo.Length - MinCompressionBytes)
                    {
                        File.Delete(targetPath);
                        return Result.NotOk($"Saved bytes ({sourceInfo.Length - newSize}) less than {MinCompressionBytes}.");
                    }

                    var similarity = GetImageSimilarity(sourcePath, targetPath, ComparisonWidth);

                    if (similarity < MinimumSimilarity)
                    {
                        File.Delete(targetPath);
                        continue;
                    }

                    return new Result()
                    {
                        Success = true,
                        Message = $"Quality changed to {quality}"
                    };
                }

                return Result.NotOk("No tried quality was good enough");
            }
            catch (Exception ex)
            {
                return Result.FromException(ex);
            }
            finally
            {
                sourceImage?.Dispose();
            }
        }

        private static double GetImageSimilarity(string path1, string path2, int comparisonWidth)
        {
            double totalSimilarity = 0;
            int width = 0, height = 0;

            using (var i1 = Image.FromFile(path1))
            {
                using (var i2 = Image.FromFile(path2))
                {
                    if (i1.Height != i2.Height || i1.Width != i2.Width)
                    {
                        return 0;
                    }

                    width = Math.Min(comparisonWidth, i1.Width - 2);
                    height = width * i1.Height / i1.Width;
                    var size = new Size(width, height);

                    using (var bmp1 = new Bitmap(i1, size))
                    using (var bmp2 = new Bitmap(i2, size))
                        for (int i = 0; i < bmp1.Width; i++)
                        {
                            for (int j = 0; j < bmp1.Height; j++)
                            {
                                var c1 = bmp1.GetPixel(i, j);
                                var c2 = bmp2.GetPixel(i, j);

                                totalSimilarity += 1 - Math.Sqrt(
                                    (c1.R - c2.R) * (c1.R - c2.R) +
                                    (c1.G - c2.G) * (c1.G - c2.G) +
                                    (c1.B - c2.B) * (c1.B - c2.B)
                                    ) / 441.67295593;
                                // 441.67295593 = 255 * SQRT(3)
                            }
                        }
                }
            }

            totalSimilarity = 100 * totalSimilarity / (width * height);
            const double threshold = 75;

            if (totalSimilarity <= threshold)
            {
                // If two images are only <= 75% similar, they are not similar at all.
                return 0;
            }
            
            // Boosting
            return (totalSimilarity - threshold) * (100.0 / (100 - threshold));
        }

        private void CloneJpeg(Image sourceImage, string targetPath, int quality)
        {
            int width = sourceImage.Size.Width,
                height = sourceImage.Size.Height;

            using (var newImage = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
            {
                using (var graphics = Graphics.FromImage(newImage))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighSpeed;

                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;

                    graphics.DrawImage(sourceImage, new Rectangle(0, 0, width, height), 0, 0, width, height, GraphicsUnit.Pixel);
                    graphics.Flush();
                }

                var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                newImage.Save(targetPath, GetEncoderForMimeType("image/jpeg"), ep);
            }
        }

        public Result CompressPng(string sourcePath, string targetPath, int timeoutSeconds = 45)
        {
            try
            {
                if (!File.Exists(this.OptipngPath))
                {
                    return Result.NotOk($"Can't find optipng.exe in {this.OptipngPath}");
                }

                if (!File.Exists(sourcePath))
                {
                    return Result.NotOk($"Can't find input file {sourcePath}");
                }

                if (sourcePath == targetPath)
                {
                    return Result.NotOk("Can't override source file now.");
                }

                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                var info = new ProcessStartInfo()
                {
                    FileName = OptipngPath,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = $"{ sourcePath } -o3 -fix -quiet -out { targetPath }"
                };

                using (var optiPng = Process.Start(info))
                {
                    if (!optiPng.WaitForExit(timeoutSeconds * 1000))
                    {
                        optiPng.Kill();
                    }
                }

                return IsWorthIt(sourcePath, targetPath, MinCompressionBytes);
            }
            catch (Exception ex)
            {
                return Result.FromException(ex);
            }
        }

        private static Result IsWorthIt(string sourcePath, string targetPath, int minCompressionBytes)
        {
            if (File.Exists(targetPath))
            {
                var sourceSize = new FileInfo(sourcePath).Length;
                var targetSize = new FileInfo(targetPath).Length;

                if (targetSize >= sourceSize - minCompressionBytes)
                {
                    File.Delete(targetPath);
                    return Result.NotOk($"Saved bytes ({sourceSize - targetSize}) less than {minCompressionBytes}.");
                }

                return Result.OK;
            }

            return Result.NotOk("Can't find target file.");
        }

        private static ImageCodecInfo GetEncoderForMimeType(string mimeType)
        {
            return ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => string.Compare(e.MimeType, mimeType, true) == 0);
        }
    }
}
