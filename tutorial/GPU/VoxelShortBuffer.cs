using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tutorial.GPU
{
    using ILGPU;
    using ILGPU.Algorithms;
    using ILGPU.Runtime;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;

    namespace tutorial.GPU
    {
        public class VoxelShortBuffer
        {
            public Vec3 scale;
            public int width;
            public int height;
            public int length;

            private dVoxelShortBuffer frameBuffer;
            private MemoryBuffer1D<Voxel, Stride1D.Dense> memoryBuffer;

            public VoxelShortBuffer(Accelerator device, int width, int height, int length, Vec3 scale)
            {
                this.scale = scale;
                this.width = width;
                this.height = height;
                this.length = length;

                memoryBuffer = device.Allocate1D<Voxel> (width * height * length);
                frameBuffer = new dVoxelShortBuffer(width, height, length, scale, memoryBuffer);
            }

            public void UpdateScale(Vec3 scale)
            {
                this.scale = scale;
                frameBuffer = new dVoxelShortBuffer(width, height, length, scale, memoryBuffer);
            }

            public dVoxelShortBuffer GetDVoxelBuffer()
            {
                return frameBuffer;
            }

            public void Dispose()
            {
                memoryBuffer.Dispose();
            }
        }

        public struct dVoxelShortBuffer
        {
            public AABB aabb;
            public Vec3 scale;
            public Vec3 position;
            public Vec3 scaledSize;
            public int width;
            public int height;
            public int length;
            public ArrayView1D<Voxel, Stride1D.Dense> data;

            public dVoxelShortBuffer(int _width, int _height, int _length, Vec3 _scale, MemoryBuffer1D<Voxel, Stride1D.Dense> _data)
            {
                scale = _scale;
                width = _width;
                height = _height;
                length = _length;


                scaledSize = new Vec3(width, height, length) * scale;
                position = new Vec3(-scaledSize.x / 2, -scaledSize.y / 2, -scaledSize.z / 2);
                aabb = new AABB(position, new Vec3(position.x + scaledSize.x, position.y + scaledSize.y, position.z + scaledSize.z));

                this.data = _data.View;
            }

            public int GetIndexFromPos(float x, float y, float z)
            {
                x = x * width;
                y = y * height;
                z = z * length;
                return GetIndexFromPos((int)x, (int)y, (int)z);
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
                return (x, y, z);
            }

            public Voxel readFrameBuffer(int x, int y, int z)
            {
                int idx = GetIndexFromPos(x, y, z);
                return data[idx];
            }

            public void writeFrameBuffer(float x, float y, float z, (byte r, byte g, byte b, byte a) value)
            {
                int idx = GetIndexFromPos(x, y, z);
                if(idx >= 0 && idx < data.Length)
                {
                    data[idx] = new Voxel(value.r, value.g, value.b);
                }
            }

            public void writeFrameBuffer(int x, int y, int z, Voxel value)
            {
                int idx = GetIndexFromPos(x, y, z);
                data[idx] = value;
            }

            public void SetVoxel(Ray ray, float depth, Voxel color, bool isLeft)
            {
                if(isLeft)
                {
                    color.SetAfterLeftCameraData(true);
                }
                else
                {
                    color.SetAfterRightCameraData(true);
                }

                if (aabb.hit(ray, depth, float.MaxValue))
                {
                    Vec3 pos = ray.a;

                    Vec3i iPos = new Vec3i(XMath.Floor(pos.x), XMath.Floor(pos.y), XMath.Floor(pos.z));

                    Vec3 step = new Vec3(ray.b.x > 0 ? 1f : -1f, ray.b.y > 0 ? 1f : -1f, ray.b.z > 0 ? 1f : -1f);

                    Vec3 tDelta = new Vec3(
                        XMath.Abs(1f / ray.b.x),
                        XMath.Abs(1f / ray.b.y),
                        XMath.Abs(1f / ray.b.z));

                    Vec3 dist = new Vec3(
                        step.x > 0 ? (iPos.x + 1 - pos.x) : (pos.x - iPos.x),
                        step.y > 0 ? (iPos.y + 1 - pos.y) : (pos.y - iPos.y),
                        step.z > 0 ? (iPos.z + 1 - pos.z) : (pos.z - iPos.z));

                    Vec3 tMax = new Vec3(
                         float.IsInfinity(tDelta.x) ? float.MaxValue : tDelta.x * dist.x,
                         float.IsInfinity(tDelta.y) ? float.MaxValue : tDelta.y * dist.y,
                         float.IsInfinity(tDelta.z) ? float.MaxValue : tDelta.z * dist.z);

                    int i = -1;
                    int max = XMath.Max(width, height) * 3;

                    while (i < max)
                    {
                        Vec3 offsetPos = pos - position;
                        if ((offsetPos.x >= 0 && offsetPos.x < width) && (offsetPos.y >= 0 && offsetPos.y < height) && (offsetPos.z >= 0 && offsetPos.z < length))
                        {
                            Voxel tile = readFrameBuffer((int)offsetPos.x, (int)offsetPos.y, (int)offsetPos.z);

                            writeFrameBuffer((int)offsetPos.x, (int)offsetPos.y, (int)offsetPos.z, tile);

                            if (tile.isClear())
                            {
                                return ;
                            }
                        }
                        else
                        {
                            return;
                        }

                        i++;

                        if (tMax.x < tMax.y)
                        {
                            if (tMax.x < tMax.z)
                            {
                                pos.x += step.x;
                                tMax.x += tDelta.x;
                            }
                            else
                            {
                                pos.z += step.z;
                                tMax.z += tDelta.z;
                            }
                        }
                        else
                        {
                            if (tMax.y < tMax.z)
                            {
                                pos.y += step.y;
                                tMax.y += tDelta.y;
                            }
                            else
                            {
                                pos.z += step.z;
                                tMax.z += tDelta.z;
                            }
                        }
                    }
                }
            }

            public Voxel hit(Ray ray, float tmin, float tmax)
            {
                if (aabb.hit(ray, tmin, tmax))
                {
                    Vec3 pos = ray.a;

                    Vec3i iPos = new Vec3i(XMath.Floor(pos.x), XMath.Floor(pos.y), XMath.Floor(pos.z));

                    Vec3 step = new Vec3(ray.b.x > 0 ? 1f : -1f, ray.b.y > 0 ? 1f : -1f, ray.b.z > 0 ? 1f : -1f);

                    Vec3 tDelta = new Vec3(
                        XMath.Abs(1f / ray.b.x),
                        XMath.Abs(1f / ray.b.y),
                        XMath.Abs(1f / ray.b.z));

                    Vec3 dist = new Vec3(
                        step.x > 0 ? (iPos.x + 1 - pos.x) : (pos.x - iPos.x),
                        step.y > 0 ? (iPos.y + 1 - pos.y) : (pos.y - iPos.y),
                        step.z > 0 ? (iPos.z + 1 - pos.z) : (pos.z - iPos.z));

                    Vec3 tMax = new Vec3(
                         float.IsInfinity(tDelta.x) ? float.MaxValue : tDelta.x * dist.x,
                         float.IsInfinity(tDelta.y) ? float.MaxValue : tDelta.y * dist.y,
                         float.IsInfinity(tDelta.z) ? float.MaxValue : tDelta.z * dist.z);

                    int i = -1;
                    int max = XMath.Max(width, height) * 3;

                    while (i < max)
                    {
                        Vec3 offsetPos = pos - position;
                        if ((offsetPos.x >= 0 && offsetPos.x < width) && (offsetPos.y >= 0 && offsetPos.y < height) && (offsetPos.z >= 0 && offsetPos.z < length))
                        {
                            Voxel tile = readFrameBuffer((int)offsetPos.x, (int)offsetPos.y, (int)offsetPos.z);

                            if (!tile.isClear())
                            {
                                return tile;
                            }
                        }

                        i++;

                        if (tMax.x < tMax.y)
                        {
                            if (tMax.x < tMax.z)
                            {
                                pos.x += step.x;
                                tMax.x += tDelta.x;
                            }
                            else
                            {
                                pos.z += step.z;
                                tMax.z += tDelta.z;
                            }
                        }
                        else
                        {
                            if (tMax.y < tMax.z)
                            {
                                pos.y += step.y;
                                tMax.y += tDelta.y;
                            }
                            else
                            {
                                pos.z += step.z;
                                tMax.z += tDelta.z;
                            }
                        }
                    }

                    return default;
                }
                else
                {
                    return default;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, Size = 4)]
        public struct Voxel
        {
            public byte r;
            public byte g;
            public byte b;
            public byte state;

            public Voxel()
            {
                r = 0;
                g = 0;
                b = 0;
                state = 0;
            }

            public Voxel(byte r, byte g, byte b)
            {
                this.r = r;
                this.g = g;
                this.b = b;
                state = 0;
                SetClear(false);
            }

            public bool isClear()
            {
                return !((state & 0b1000_0000) > 0);
            }

            public void SetClear(bool val)
            {
                if(!val)
                {
                    state |= 0b1000_0000;
                }
                else
                {
                    state &= 0b0111_1111;
                }
            }

            public bool isAfterLeftCameraData()
            {
                return (state & 0b0100_0000) > 0;
            }

            public void SetAfterLeftCameraData(bool val)
            {
                if (!val)
                {
                    state |= 0b0100_0000;
                }
                else
                {
                    state &= 0b1011_1111;
                }
            }

            public bool isAfterRightCameraData()
            {
                return (state & 0b0010_0000) > 0;
            }

            public void SetAfterRightCameraData(bool val)
            {
                if (!val)
                {
                    state |= 0b0010_0000;
                }
                else
                {
                    state &= 0b1101_1111;
                }
            }
        }
    }

}
