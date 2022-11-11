using ILGPU;
using ILGPU.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tutorial.GPU
{
    public class PixelBuffer2D<T>
        : IDisposable
        where T : unmanaged
    {
        public int width;
        public int height;

        public long byteLength;

        private bool needsUpdate = false;
        private bool needsTransfer = false;

        private T[] data;
        private dPixelBuffer2D<T> frameBuffer;
        public MemoryBuffer1D<T, Stride1D.Dense> memoryBuffer;

        public PixelBuffer2D(Accelerator device, int height, int width)
        {
            this.width = width;
            this.height = height;

            unsafe
            {
                byteLength = width * height * 3 * sizeof(T);
            }

            data = new T[width * height * 3];
            memoryBuffer = device.Allocate1D<T>(width * height * 3);
            frameBuffer = new dPixelBuffer2D<T>(width, height, memoryBuffer);
        }

        public static unsafe void Save(PixelBuffer2D<byte> pixelBuffer, string path)
        {
            try
            {
                Directory.CreateDirectory(Directory.GetParent(path)!.FullName);

                fixed (byte* bytes = pixelBuffer.GetRawFrameData())
                {
                    using Bitmap b = new Bitmap(pixelBuffer.width, pixelBuffer.height, pixelBuffer.width * 3, PixelFormat.Format24bppRgb, new IntPtr(bytes));
                    b.Save(path, ImageFormat.Jpeg);  
                }
            }
            catch(Exception e) 
            {
                Console.WriteLine(e);
            }
            
        }

        public ref T[] GetRawFrameData()
        {
            if (needsTransfer)
            {
                memoryBuffer.CopyToCPU(data);
                needsTransfer = false;
            }
            return ref data;
        }

        public dPixelBuffer2D<T> GetDPixelBuffer()
        {
            if(needsUpdate)
            {
                memoryBuffer.CopyFromCPU(data);
                needsUpdate = false;
            }

            needsTransfer = true;
            return frameBuffer;
        }

        public T this[int index]
        {
            get
            {
                if(needsTransfer)
                {
                    memoryBuffer.CopyToCPU(data);
                    needsTransfer = false;
                }
                return data[index];
            }

            set
            {
                needsUpdate = true;
                data[index] = value;
            }
        }

        public void Dispose()
        {
            if(!memoryBuffer.IsDisposed)
            {
                try
                {
                    memoryBuffer.Dispose();
                }
                catch(Exception e)
                {
                    Trace.WriteLine(e.ToString());
                }
            }
        }
    }

    public struct dPixelBuffer2D<T> where T : unmanaged
    {
        public int width;
        public int height;
        public ArrayView1D<T, Stride1D.Dense> frame;

        public dPixelBuffer2D(int width, int height, MemoryBuffer1D<T, Stride1D.Dense> frame)
        {
            this.width = width;
            this.height = height;
            this.frame = frame.View;
        }

        public (int x, int y) GetPosFromIndex(int index)
        {
            int x = index % width;
            int y = index / width;

            return (x, y);
        }

        public int GetIndexFromPos(int x, int y)
        {
            return ((y * width) + x);
        }

        public (T x, T y, T z) readFrameBuffer(int index)
        {
            int subPixel = index * 3;
            return (frame[subPixel], frame[subPixel + 1], frame[subPixel + 2]);
        }

        public (T x, T y, T z) readFrameBuffer(int x, int y)
        {
            int subPixel = GetIndexFromPos(x, y) * 3;
            return (frame[subPixel], frame[subPixel + 1], frame[subPixel + 2]);
        }

        public void writeFrameBuffer(int index, T r, T g, T b)
        {
            int subPixel = index * 3;
            frame[subPixel] = r;
            frame[subPixel + 1] = g;
            frame[subPixel + 2] = b;
        }

        public void writeFrameBuffer(int x, int y, T r, T g, T b)
        {
            int subPixel = GetIndexFromPos(x, y) * 3;
            int length = (int)(frame.Length - 3);

            if(subPixel >= 0 && subPixel < length)
            {
                frame[subPixel] = r;
                frame[subPixel + 1] = g;
                frame[subPixel + 2] = b;
            }
        }

        public void writeFrameBufferFlipped(int x, int y, T r, T g, T b)
        {
            int subPixel = ((y * width) + (width - x)) * 3;
            frame[subPixel] = r;
            frame[subPixel + 1] = g;
            frame[subPixel + 2] = b;
        }
    }

}
