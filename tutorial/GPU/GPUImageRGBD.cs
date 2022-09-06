using ILGPU.Runtime;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tutorial.GPU
{
    public class GPUImageRGBD
    {
        public string path;
        public Bitmap bitmap;
        public PixelBuffer2D<byte> image;
        public PixelBuffer2D<byte> depth;

        public GPUImageRGBD(Accelerator device, string path)
        {
            this.path = path;
            bitmap = new Bitmap(path);
            image = new PixelBuffer2D<byte>(device, bitmap.Height, bitmap.Width / 2);
            depth = new PixelBuffer2D<byte>(device, bitmap.Height, bitmap.Width / 2);
            CopyImageSlow();
        }

        private void CopyImageSlow()
        {
            Parallel.For(0, bitmap.Width * bitmap.Height, i =>
            {
                int x = i % bitmap.Width;
                int y = i / bitmap.Width;

                Color color = bitmap.GetPixel(x, y);
                int subPixel = (i * 3) / 2;

                //Depth
                if (x < bitmap.Width / 2)
                {
                    image[subPixel] = color.R;
                    image[subPixel + 1] = color.G;
                    image[subPixel + 2] = color.B;
                }
                else
                {
                    depth[subPixel] = color.R;
                    depth[subPixel + 1] = color.G;
                    depth[subPixel + 2] = color.B;
                }
            });
        }
    }
}
