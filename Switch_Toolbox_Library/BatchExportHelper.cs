using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace Toolbox.Library
{
    public static class BatchExportHelper
    {
        private static readonly List<ExportableTexture> Textures = new List<ExportableTexture>();
        public static bool IsActive = false;

        public static void ExportAll()
        {
            var threads = new List<Thread>();
            var threadSplit = Split(Textures, Environment.ProcessorCount - 1);

            foreach (var textures in threadSplit)
            {
                threads.Add(new Thread(o =>
                {
                    foreach (var tex in textures)
                    {
                        tex.Save();
                    }
                })
                {
                    Name = "Texture Batch Export Thread " + threads.Count
                });
            }
            
            Textures.Clear();
            GC.Collect();
        }

        private static IEnumerable<IEnumerable<T>> Split<T>(this IEnumerable<T> list, int parts)
        {
            var i = 0;
            var splits = from item in list
                group item by i++ % parts
                into part
                select part.AsEnumerable();
            return splits;
        }

        public static void Add(Bitmap bitmap, string path)
        {
            Textures.Add(new ExportableTexture(path, bitmap));
        }
    }

    public class ExportableTexture
    {
        private readonly string _path;
        private readonly Bitmap _bitmap;

        public ExportableTexture(string path, Bitmap bitmap)
        {
            _path = path;
            _bitmap = bitmap;
        }

        public void Save()
        {
            _bitmap.Save(_path);
            _bitmap.Dispose();
        }
    }
}