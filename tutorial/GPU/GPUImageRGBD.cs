using ILGPU.Runtime;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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

        private unsafe void CopyImageSlow()
        {
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            Parallel.For(0, data.Height, y =>
            {
                ReadOnlySpan<byte> bytes = new ReadOnlySpan<byte>(data.Scan0.ToPointer(), data.Stride * data.Height);

                for (int x = 0; x < data.Width; x++)
                {
                    int inSubPixel = (y * data.Stride + (x * 3));

                    Color color = Color.FromArgb(bytes[inSubPixel], bytes[inSubPixel + 1], bytes[inSubPixel + 2]);

                    if (x < data.Width / 2)
                    {
                        int outSubPixel = (((y * image.width) + (x)) * 3);

                        image[outSubPixel] = color.R;
                        image[outSubPixel + 1] = color.G;
                        image[outSubPixel + 2] = color.B;
                    }
                    else
                    {
                        int outSubPixel = (((y * depth.width) + (x - image.width)) * 3);

                        depth[outSubPixel] = color.R;
                        depth[outSubPixel + 1] = color.G;
                        depth[outSubPixel + 2] = color.B;
                    }
                }
            });

            bitmap.UnlockBits(data);
        }
    }
}
