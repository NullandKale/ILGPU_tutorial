using System;
using ILGPU;
using ILGPU.Runtime;

namespace tutorial.GPU
{
    public struct FrameBuffer
    {
        public bool locked;
        public ushort minDepth;
        public ushort maxDepth;
        public int width;
        public int height;
        public bool filtered;

        public ArrayView1D<byte, Stride1D.Dense> color;
        public ArrayView1D<ushort, Stride1D.Dense> depth;

        public FrameBuffer(int width, int height,
                           ushort minDepth, ushort maxDepth,
                           ArrayView1D<byte, Stride1D.Dense> color, ArrayView1D<ushort, Stride1D.Dense> depth)
        {
            locked = false;
            filtered = false;
            this.minDepth = minDepth;
            this.maxDepth = maxDepth;
            this.width = width;
            this.height = height;
            this.color = color;
            this.depth = depth;
        }

        public Vec3 GetColor(float x, float y)
        {
            var c = GetColorPixel((int)(x * width), (int)(y * height));

            return new Vec3(c.r / 255.0f, c.g / 255.0f, c.b / 255.0f);
        }

        public (byte r, byte g, byte b, byte a) GetColorPixel(float x, float y)
        {
            return GetColorPixel((int)(x * width), (int)(y * height));
        }

        public (byte r, byte g, byte b, byte a) GetColorPixel(int x, int y)
        {
            return GetColorPixel(y * width + x);
        }

        public (byte r, byte g, byte b, byte a) GetColorPixel(int index)
        {
            index *= 4;

            if (index >= 0 && index < color.Length)
            {
                return (color[index],
                    color[index + 1],
                    color[index + 2],
                    color[index + 3]);
            }
            else
            {
                return (1, 0, 1, 0);
            }
        }

        public static float Remap(float source, float sourceFrom, float sourceTo, float targetFrom, float targetTo)
        {
            return targetFrom + (source - sourceFrom) * (targetTo - targetFrom) / (sourceTo - sourceFrom);
        }

        public ushort FilterDepthPixel(int x, int y, int filterWidth, int filterHeight, int fuzz)
        {
            ushort depth = GetDepthPixel(x, y);

            if (depth == 0)
            {
                float accumulator = 0;
                int count = 0;
                int fuzzCounter = 0;

                int xMax = x + filterWidth;
                int xMin = x - filterWidth;

                int yMax = y + filterHeight;
                int yMin = y - filterHeight;

                for (int i = y; i < yMax; i++)
                {
                    ushort c = GetDepthPixel(x, i);
                    if (c != 0)
                    {
                        accumulator += c;
                        count++;
                        fuzzCounter++;
                        if (fuzzCounter > fuzz)
                        {
                            i = yMax;
                        }
                    }

                }

                fuzzCounter = 0;

                for (int i = y; i > yMin; i--)
                {
                    ushort c = GetDepthPixel(x, i);
                    if (c != 0)
                    {
                        accumulator += c;
                        count++;
                        fuzzCounter++;
                        if (fuzzCounter > fuzz)
                        {
                            i = yMin;
                        }
                    }
                }

                fuzzCounter = 0;

                for (int i = x; i < xMax; i++)
                {
                    ushort c = GetDepthPixel(i, y);
                    if (c != 0)
                    {
                        accumulator += c;
                        count++;
                        fuzzCounter++;
                        if (fuzzCounter > fuzz)
                        {
                            i = xMax;
                        }
                    }
                }

                fuzzCounter = 0;

                for (int i = x; i > xMin; i--)
                {
                    ushort c = GetDepthPixel(i, y);
                    if (c != 0)
                    {
                        accumulator += c;
                        count++;
                        fuzzCounter++;
                        if (fuzzCounter > fuzz)
                        {
                            i = xMin;
                        }
                    }
                }

                return (ushort)(accumulator / count);
            }

            return depth;
        }

        public float GetDepthPixel(float x, float y)
        {
            float xCord = x * width;
            float yCord = y * height;

            ushort depth = GetDepthPixel((int)xCord, (int)yCord);

            float toReturn = Remap(depth, minDepth, maxDepth, 0f, 1f);

            return toReturn;
        }

        public (byte high, byte low) GetDepthBytes(float x, float y)
        {
            float xCord = x * width;
            float yCord = y * height;

            ushort depth = GetDepthPixel((int)xCord, (int)yCord);

            byte upperByte = (byte)(depth >> 8);
            byte lowerByte = (byte)(depth & 0xFF);

            return (upperByte, lowerByte);
        }

        public ushort GetDepthPixel(int xCord, int yCord)
        {
            return GetDepthPixel(yCord * width + xCord);
        }

        public void SetDepthPixel(int xCord, int yCord, ushort val)
        {
            SetDepthPixel(yCord * width + xCord, val);
        }

        public void SetDepthPixel(int index, ushort val)
        {
            if (index >= 0 && index < depth.Length)
            {
                depth[index] = val;
            }
        }

        public ushort GetDepthPixel(int x, int y, int filterSize)
        {
            int count = filterSize;
            float val = 0;
            int samples = 0;

            for (int i = -count; i <= count; i++)
            {
                for (int j = -count; j <= count; j++)
                {
                    int yCord = y + i;
                    int xCord = x + j;

                    if (yCord >= 0 && yCord < height && xCord >= 0 && xCord < width)
                    {
                        int depth = GetDepthPixel(yCord * width + xCord);
                        if (depth != 0)
                        {
                            val += depth;
                            samples++;
                        }
                    }
                }
            }

            if (samples == 0)
            {
                return 0;
            }

            return (ushort)(val / samples);

            //return GetDepthPixel(y * depthWidth + x);
        }

        public ushort GetDepthPixel(int index)
        {
            if (index >= 0 && index < depth.Length)
            {
                return depth[index];
            }
            else
            {
                return 0;
            }
        }
    }
}
