using System;
using System.Drawing;

namespace PdfStage
{
    internal static class PageGeometry
    {
        // Both the native window and the raster use the same integer-pixel boundary.
        // Re-fitting a rounded size must not shave off another pixel.
        internal static Size Fit(Size box, double ratio)
        {
            int w = Math.Max(1, box.Width), h = Math.Max(1, box.Height);
            if (!(ratio > 0) || Double.IsInfinity(ratio)) throw new ArgumentOutOfRangeException("ratio");
            if (Math.Abs(w / ratio - h) <= 0.5 || Math.Abs(h * ratio - w) <= 0.5) return new Size(w,h);
            return w / ratio <= h
                ? new Size(w, Math.Max(1, (int)Math.Round(w / ratio)))
                : new Size(Math.Max(1, (int)Math.Round(h * ratio)), h);
        }
    }
}
