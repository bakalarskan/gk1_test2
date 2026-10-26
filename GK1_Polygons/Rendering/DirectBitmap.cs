using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PolygonEditor.Rendering
{
    /// <summary>
    /// Szybki bufor bitmapowy z bezpośrednim dostępem do tablicy pikseli 32-bit ARGB.
    /// Całkowicie eliminuje wywołania Bitmap.SetPixel.
    /// </summary>
    public sealed class DirectBitmap : IDisposable
    {
        public Bitmap Bitmap { get; private set; }
        public int[] Bits { get; private set; }
        public bool Disposed { get; private set; }
        public int Height { get; private set; }
        public int Width { get; private set; }

        private GCHandle _bitsHandle;

        public DirectBitmap(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            Bits = new int[Width * Height];
            _bitsHandle = GCHandle.Alloc(Bits, GCHandleType.Pinned);
            Bitmap = new Bitmap(
                Width,
                Height,
                Width * 4,
                PixelFormat.Format32bppPArgb,
                _bitsHandle.AddrOfPinnedObject());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPixel(int x, int y, int argbColor)
        {
            if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            {
                Bits[y * Width + x] = argbColor;
            }
        }

        public void Clear(Color color)
        {
            Array.Fill(Bits, color.ToArgb());
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Bitmap.Dispose();
            _bitsHandle.Free();
        }
    }
}