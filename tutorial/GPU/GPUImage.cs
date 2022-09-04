using ILGPU.Runtime;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tutorial.GPU
{
    internal class GPUImage
    {
        public string path;
        public Bitmap bitmap;
        public PixelBuffer2D<byte> data;

        public GPUImage(Accelerator device, string path)
        {
            this.path = path;
            bitmap = new Bitmap(path);
            data = new PixelBuffer2D<byte>(device, bitmap.Height, bitmap.Width);
            CopyImageSlow();
        }

        private void CopyImageSlow()
        {
            Parallel.For(0, bitmap.Width * bitmap.Height, i =>
            {
                int x = i % bitmap.Width;
                int y = i / bitmap.Width;
                Color color = bitmap.GetPixel(x, y);
                int subPixel = i * 3;
                data[i] = color.R;
                data[i + 1] = color.G;
                data[i + 2] = color.B;
            });
        }
    }
}
