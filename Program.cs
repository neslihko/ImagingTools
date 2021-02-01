namespace ImagingTools
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Diagnostics;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Linq;

    class Program
    {
        private static string GetConfig(string key, string defaultValue = null) => ConfigurationManager.AppSettings[key] ?? defaultValue;

        static void Main(string[] args)
        {
            var workingFolder = GetConfig("WorkingFolder");
            var optimizerPath = GetConfig("OptipngPath");
            var minJpegSimilarity = Convert.ToDouble(GetConfig("MinJpegSimilarity", "96"));
            var qualityCheckWidth = Convert.ToInt32(GetConfig("QualityCheckWidth", "500"));
            var meaningfulFileSizeChange = Convert.ToInt32(GetConfig("MeaningfulFileSizeChange", "2048"));
            var onlyTopDirectory = Convert.ToBoolean(GetConfig("OnlyTopDirectory", "false"));
            var overrideFiles = Convert.ToBoolean(GetConfig("OverrideFiles", "true"));
            var deleteUnoptimizable = Convert.ToBoolean(GetConfig("DeleteUnoptimizable", "true"));
            var triedJpegQualities = new List<int>(GetConfig("TriedJpegQualities", "65,75,80,85,90")
               .Split(',', ' ', ';')
               .Where(s => int.TryParse(s.Trim(), out _))
               .Select(s => Convert.ToInt32(s.Trim())));

            Console.WriteLine($"Starting with parameters:");
            Console.WriteLine($"{"Target folder",30}:\t{workingFolder}");
            Console.WriteLine($"{"Path to optipng.exe",30}:\t{optimizerPath}");
            Console.WriteLine($"{"Min. jpeg similarity",30}:\t{minJpegSimilarity} %");
            Console.WriteLine($"{"Meaningful file size change",30}:\t{meaningfulFileSizeChange} bytes");
            Console.WriteLine($"{"Only top directory",30}:\t{onlyTopDirectory}");
            Console.WriteLine($"{"Override files",30}:\t{overrideFiles}");
            Console.WriteLine($"{"Delete unoptimized",30}:\t{deleteUnoptimizable}");
            Console.WriteLine($"{"Tried jpeg qualities",30}:\t{string.Join(", ", triedJpegQualities)} %");
            Console.WriteLine("Press any key to start...");

            Console.ReadKey();

            var optimizer = new ImageOptimizer(optimizerPath,
                workingFolder,
                minJpegSimilarity,
                qualityCheckWidth,
                meaningfulFileSizeChange,
                triedJpegQualities);

            optimizer.CompressImagesInFolder(onlyTopDirectory,
                overrideFiles,
                deleteUnoptimizable);

            Console.WriteLine("Completed, press any key to exit...");
            Console.ReadKey();
        }
    }
}