using ILGPU;
using ILGPU.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tutorial.GPU
{
    public class VoxelBuffer<T>
        : IDisposable
        where T : unmanaged
    {
        public int width;
        public int height;
        public int length;

        private dVoxelBuffer<T> frameBuffer;
        private MemoryBuffer1D<T, Stride1D.Dense> memoryBuffer;

        public VoxelBuffer(Accelerator device, int width, int height, int length)
        {
            this.width = width;
            this.height = height;
            this.length = length;

            memoryBuffer = device.Allocate1D<T>(width * height * length);
            frameBuffer = new dVoxelBuffer<T>(width, height, length, memoryBuffer);
        }

        public dVoxelBuffer<T> GetDVoxelBuffer()
        {
            return frameBuffer;
        }

        public void Dispose()
        {
            memoryBuffer.Dispose();
        }
    }

    public struct dVoxelBuffer<T> where T : unmanaged
    {
        public int width;
        public int height;
        public int length;
        public ArrayView1D<T, Stride1D.Dense> data;

        public dVoxelBuffer(int width, int height, int length, MemoryBuffer1D<T, Stride1D.Dense> data)
        {
            this.width = width;
            this.height = height;
            this.length = length;

            this.data = data.View;
        }

        public int GetIndexFromPos(int x, int y, int z)
        {
            return (z * width * height) + (y * width) + x;
        }

        public (int x, int y, int z) GetPosFromIndex(int index)
        {
            int z = index / (width * height);
            index -= (z * width * height);
            int y = index / width;
            int x = index % width;
            return ( x, y, z );
        }

        public T readFrameBuffer(int x, int y, int z)
        {
            int idx = GetIndexFromPos(x, y, z);
            return data[idx];
        }

        public void writeFrameBuffer(int x, int y, int z, T value)
        {
            int idx = GetIndexFromPos(x, y, z);
            data[idx] = value;
        }
    }
}
