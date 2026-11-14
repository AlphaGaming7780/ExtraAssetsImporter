using Colossal.AssetPipeline.Importers;
using ExtraLib.Debugger;
using System.Collections.Generic;
using System.Threading;

namespace ExtraAssetsImporter.AssetImporter.Utils
{
    // The game's texture importer decodes, builds the mips, and compresses on the calling thread,
    // and holds the image's buffers until it returns: an import takes one core and the memory of
    // its image. Every importer, new and old, imports through these slots, so their count limits
    // the cores and memory in use however many assets the importers run.
    internal static class TextureImportSlots
    {
        private static readonly object s_Lock = new();
        private static readonly List<Waiter> s_Waiters = new();
        private static int s_Free;
        private static long s_NextOrder;

        internal static int Count { get; private set; }

        // A pass can start while the old importers of the previous one still hold slots, so the
        // free count moves by the difference.
        internal static void SetCount(int count)
        {
            lock (s_Lock)
            {
                s_Free += count - Count;
                Count = count;
                Monitor.PulseAll(s_Lock);
            }
        }

        // The import runs below the game's threads in priority: on a busy CPU the game's frame
        // comes first, and the import gets the cores it leaves idle.
        internal static TextureImporter.Texture Import(DefaultTextureImporter importer, DefaultTextureImporter.ImportSettings settings, string filePath)
        {
            Acquire(GetPixelCount(filePath));

            Thread thread = Thread.CurrentThread;
            ThreadPriority priority = thread.Priority;
            thread.Priority = ThreadPriority.BelowNormal;
            try
            {
                // The Texture.Import timer starts after Acquire, so it times the import alone.
                using (PerformanceStats.MeasureCurrent("Texture.Import"))
                    return importer.Import(settings, filePath);
            }
            finally
            {
                thread.Priority = priority;
                Release();
            }
        }

        // A freed slot goes to the waiting import with the most pixels: the largest maps take the
        // longest and decide when the pass ends, so they start first and the small ones fill the
        // gaps.
        private static void Acquire(long pixels)
        {
            lock (s_Lock)
            {
                Waiter waiter = new() { pixels = pixels, order = s_NextOrder++ };
                s_Waiters.Add(waiter);
                while (s_Free <= 0 || GetNextWaiter() != waiter)
                    Monitor.Wait(s_Lock);

                s_Waiters.Remove(waiter);
                s_Free--;

                // The next waiter in line may take another free slot.
                if (s_Free > 0 && s_Waiters.Count > 0) Monitor.PulseAll(s_Lock);
            }
        }

        private static void Release()
        {
            lock (s_Lock)
            {
                s_Free++;
                Monitor.PulseAll(s_Lock);
            }
        }

        private static Waiter GetNextWaiter()
        {
            Waiter next = null;
            foreach (Waiter waiter in s_Waiters)
            {
                if (next == null || waiter.pixels > next.pixels || (waiter.pixels == next.pixels && waiter.order < next.order)) next = waiter;
            }

            return next;
        }

        // Anything but a readable PNG counts as zero pixels and waits behind the PNGs, which only
        // delays it.
        private static long GetPixelCount(string filePath)
        {
            return ImportersUtils.TryReadPngSize(filePath, out int width, out int height) ? (long)width * height : 0;
        }

        private class Waiter
        {
            public long pixels;
            public long order;
        }
    }
}
