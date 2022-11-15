using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using tutorial.GPU;
using tutorial.Kinect;

namespace tutorial
{
    public class Renderer
    {
        public MainWindow window;
        public Context context;
        public Accelerator device;

        public Action<Index2D, Camera, int, dPixelBuffer2D<byte>, dVoxelShortBuffer> generateFrameKernel;
        public Action<Index2D, Camera, bool, Vec3, FrameBuffer, dVoxelShortBuffer> fillVoxelsKernel;
        public Action<Index2D, dVoxelShortBuffer> clearVoxelsKernel;
        public Action<Index2D, Camera, bool, dPixelBuffer2D<byte>, FrameBuffer> generateTestKernel;
        public Action<Index2D, FrameBuffer, SpecializedValue<int>, SpecializedValue<int>> filterDepthMapKernel;

        public Camera camera;
        public Camera stationaryCamera;
        public VoxelShortBuffer voxelBuffer;
        public PixelBuffer2D<byte> frameBuffer;
        public KinectManager kinectManager;

        public volatile bool shuttingDown = false;
        public volatile bool run = true;
        public volatile bool pause = false;

        public bool doFilter = false;

        public Thread renderThread;

        public int renderState = 2;
        public int kinect = 0;

        public Renderer(MainWindow window, bool forceCPU)
        {
            this.window = window;
            context = Context.Create(builder => builder.Cuda().CPU().EnableAlgorithms().Optimize(OptimizationLevel.O2));
            device = context.GetPreferredDevice(forceCPU).CreateAccelerator(context);
            kinectManager = new KinectManager(device);

            voxelBuffer = new VoxelShortBuffer(device, kinectManager.Width, kinectManager.Height, 256, new Vec3(1, 1, 1));
            fillVoxelsKernel = device.LoadAutoGroupedStreamKernel<Index2D, Camera, bool, Vec3, FrameBuffer, dVoxelShortBuffer>(FillVoxelBufferFromCamera);
            clearVoxelsKernel = device.LoadAutoGroupedStreamKernel<Index2D, dVoxelShortBuffer>(ClearVoxelBuffer);
            generateFrameKernel = device.LoadAutoGroupedStreamKernel<Index2D, Camera, int, dPixelBuffer2D<byte>, dVoxelShortBuffer>(GenerateFrame);
            //generateFrameKernel = device.LoadAutoGroupedStreamKernel<Index2D, Camera, int, dPixelBuffer2D<byte>, dVoxelShortBuffer>(GenerateOrthographicFrame);
            generateTestKernel = device.LoadAutoGroupedStreamKernel<Index2D, Camera, bool, dPixelBuffer2D<byte>, FrameBuffer>(GenerateTestFrame);
            filterDepthMapKernel = device.LoadAutoGroupedStreamKernel<Index2D, FrameBuffer, SpecializedValue<int>, SpecializedValue<int>>(FilterDepthMap);

        }

        public void TakePhotoset()
        {
            pause = true;
            camera.isBGR = true;

            float movement = 25;
            int totalFrames = 75;

            camera = new Camera(camera, new Vec3(movement * totalFrames / 2f, 0, 0), new Vec3());

            for (int i = 0; i < totalFrames; i++)
            {
                camera = new Camera(camera, new Vec3(-movement, 0, 0), new Vec3());

                generateFrameKernel(new Index2D(frameBuffer.width - 1, frameBuffer.height - 1), camera, window.depth, frameBuffer.GetDPixelBuffer(), voxelBuffer.GetDVoxelBuffer());
                device.Synchronize();
                if (i < 10)
                {
                    PixelBuffer2D<byte>.Save(frameBuffer, "C:\\Repos\\tmp\\out\\capture0" + i + ".jpg");
                }
                else
                {
                    PixelBuffer2D<byte>.Save(frameBuffer, "C:\\Repos\\tmp\\out\\capture" + i + ".jpg");
                }
            }
            camera.isBGR = false;
            pause = false;
        }

        public void Start(int width, int height)
        {
            if (renderThread != null)
            {
                run = false;
                renderThread.Join(1000);
            }

            if (frameBuffer != null)
            {
                frameBuffer.Dispose();
            }

            switch (renderState)
            {
                case 0:
                case 1:
                    width = kinectManager.Width;
                    height = kinectManager.Height;
                    break;
                case 2:
                    break;
            }

            if (device.IsDisposed)
            {
                Trace.WriteLine("WTF");
                return;
            }

            frameBuffer = new PixelBuffer2D<byte>(device!, height, width);

            Vec3 origin = new Vec3(0, 0, voxelBuffer.GetDVoxelBuffer().aabb.max.z * 3);
            Vec3 lookAt = new Vec3(0, 0, voxelBuffer.GetDVoxelBuffer().aabb.max.z);

            camera = new Camera(origin, lookAt, new Vec3(0, -1, 0), width, height, 110f, new Vec3(0, 0, 0));
            stationaryCamera = new Camera(camera, 110f);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                window.frame.UpdateResolution(width, height);
                window.frame.update(device, frameBuffer);
            });

            run = true;
            renderThread = new Thread(updateRenderThread);
            renderThread.IsBackground = true;
            renderThread.Start();
        }

        private void RenderFrame()
        {
            switch (renderState)
            {
                case 0:
                case 1:
                    RenderFrameBuffer(ref kinectManager.GetFrameBuffer(kinect), kinect == 0);
                    break;
                case 2:
                    RenderFrameBuffer(ref kinectManager.GetFrameBuffer(0), true);
                    RenderFrameBuffer(ref kinectManager.GetFrameBuffer(1), false);
                    generateFrameKernel(new Index2D(frameBuffer.width - 1, frameBuffer.height - 1), camera, window.depth, frameBuffer.GetDPixelBuffer(), voxelBuffer.GetDVoxelBuffer());
                    break;
            }

            device.Synchronize();

            if(!pause)
            {
                clearVoxelsKernel(new Index2D(kinectManager.Width, kinectManager.Height), voxelBuffer.GetDVoxelBuffer());
            }
        }

        private void RenderFrameBuffer(ref FrameBuffer currentFrame, bool isLeft)
        {
            currentFrame.locked = true;

            if (!currentFrame.filtered)
            {
                if (doFilter)
                {
                    filterDepthMapKernel(new Index2D(kinectManager.Width - 1, kinectManager.Height - 1), currentFrame, new SpecializedValue<int>(2), new SpecializedValue<int>(15));
                }

                currentFrame.filtered = true;
            }

            switch (renderState)
            {
                case 0:
                    generateTestKernel(new Index2D(frameBuffer.width - 1, frameBuffer.height - 1), camera, false, frameBuffer.GetDPixelBuffer(), currentFrame);
                    break;
                case 1:
                    generateTestKernel(new Index2D(frameBuffer.width - 1, frameBuffer.height - 1), camera, true, frameBuffer.GetDPixelBuffer(), currentFrame);
                    break;
                case 2:
                    fillVoxelsKernel(new Index2D(kinectManager.Width - 1, kinectManager.Height - 1), stationaryCamera, isLeft, window.offset, currentFrame, voxelBuffer.GetDVoxelBuffer());
                    break;
            }

            device.Synchronize();

            currentFrame.locked = false;
        }

        private void DrawFrame(Stopwatch timer)
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    window.frame.frameTime = timer.Elapsed.Milliseconds;
                    window.frame.update(device, frameBuffer);
                });
            }
            catch
            {
                run = false;
            }
        }
        private void updateRenderThread()
        {
            Stopwatch timer = new Stopwatch();

            while (run)
            {
                if (pause)
                {
                    Thread.Sleep(250);
                }
                else
                {
                    timer.Start();

                    RenderFrame();

                    timer.Stop();

                    DrawFrame(timer);

                }

                int timeToSleep = (int)(1000.0 / 60.0 - timer.Elapsed.TotalMilliseconds);

                if (timeToSleep > 0)
                {
                    Thread.Sleep(timeToSleep);
                }

                timer.Reset();
            }

            if (shuttingDown)
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            voxelBuffer.Dispose();
            frameBuffer.Dispose();

            kinectManager.Dispose();
            device.Dispose();
            context.Dispose();
        }

        private static void ClearVoxelBuffer(Index2D pixel, dVoxelShortBuffer voxels)
        {
            for (int z = 0; z < voxels.length; z++)
            {
                //if(pixel.X == 0 || pixel.Y == 0 || z == 0)
                //{
                //    Voxel v = new Voxel(255, 0, 255);
                //    v.SetAfterLeftCameraData(true);
                //    v.SetAfterRightCameraData(true);
                //    voxels.writeFrameBuffer(pixel.X, pixel.Y, z, v);
                //}
                //else
                //{
                    voxels.writeFrameBuffer(pixel.X, pixel.Y, z, default);
                //}
            }
        }

        private static void FilterDepthMap(Index2D pixel, FrameBuffer frameBuffer, SpecializedValue<int> filterDistance, SpecializedValue<int> passes)
        {
            for (int i = 0; i < passes; i++)
            {
                ushort filteredDepth = frameBuffer.FilterDepthPixel(pixel.X, pixel.Y, filterDistance, filterDistance, 2);
                frameBuffer.SetDepthPixel(pixel.X, pixel.Y, filteredDepth);
            }
        }

        private static void FillVoxelBufferFromCamera(Index2D pixel, Camera camera, bool isLeft, Vec3 offset, FrameBuffer frameBuffer, dVoxelShortBuffer voxels)
        {
            float x = ((float)pixel.X / (float)frameBuffer.width);
            float y = ((float)pixel.Y / (float)frameBuffer.height);

            float depth = frameBuffer.GetDepthPixel(x, y);

            float maxOffset = offset.x;
            float baseOffset = offset.y;

            if (isLeft)
            {
                maxOffset *= -1;
                baseOffset *= -1;
            }

            Camera movedCamera = new Camera(camera, new Vec3(baseOffset + (maxOffset * depth), 0, -voxels.aabb.max.z * 3), new Vec3());

            //depth = 1.0f - depth;

            if (depth > 0 && depth < camera.depthCutOff)
            {
                Vec3 colorData = frameBuffer.GetColor(x, y);
                Ray ray = movedCamera.GetRay(pixel.X, pixel.Y);

                voxels.SetVoxel(ray, depth, new Voxel(colorData.x, colorData.y, colorData.z), isLeft, (int)offset.z, 50);
            }
        }

        private static void GenerateFrame(Index2D pixel, Camera camera, int depth, dPixelBuffer2D<byte> output, dVoxelShortBuffer voxels)
        {
            Ray ray = camera.GetRay(pixel.X, pixel.Y);
            VoxelHit hit = voxels.hit(ray, 0.00001f, float.MaxValue, depth);
            Vec3 color = hit.GetColormappedColor(false);

            byte r = (byte)(color.x);
            byte g = (byte)(color.y);
            byte b = (byte)(color.z);

            if (color.x > 255 || color.y > 255 || color.z > 255)
            {
                r = 255;
                g = 0;
                b = 255;
            }

            if (color.x > 255 || color.y > 255 || color.z > 255)
            {
                r = 255;
                g = 0;
                b = 255;
            }

            if (camera.isBGR)
            {
                output.writeFrameBuffer(pixel.X, pixel.Y, r, g, b);
            }
            else
            {
                output.writeFrameBuffer(pixel.X, pixel.Y, b, g, r);
            }
        }

        private static void GenerateTestFrame(Index2D pixel, Camera camera, bool testDepth, dPixelBuffer2D<byte> output, FrameBuffer frameBuffer)
        {
            (byte r, byte g, byte b, byte a) color = default;

            if (testDepth)
            {
                float x = ((float)pixel.X / (float)frameBuffer.width);
                float y = ((float)pixel.Y / (float)frameBuffer.height);

                float depth = frameBuffer.GetDepthPixel(x, y);

                bool failed1 = false;
                bool failed2 = false;

                if (depth <= 0)
                {
                    failed1 = true;
                }
                else if (depth >= camera.depthCutOff)
                {
                    failed2 = true;
                }

                color.r = (byte)((failed2 ? 0 : depth) * 255.0);
                color.g = (byte)((failed1 ? 0 : depth) * 255.0);
                color.b = (byte)(depth * 255.0);
            }
            else
            {
                float x = ((float)pixel.X / (float)frameBuffer.width);
                float y = ((float)pixel.Y / (float)frameBuffer.height);

                color = frameBuffer.GetColorPixel(x, y);
            }

            if (camera.isBGR)
            {
                output.writeFrameBuffer(pixel.X, pixel.Y, color.r, color.g, color.b);
            }
            else
            {
                output.writeFrameBuffer(pixel.X, pixel.Y, color.b, color.g, color.r);
            }
        }

        private static void GenerateOrthographicFrame(Index2D pixel, Camera camera, int unused, dPixelBuffer2D<byte> output, dVoxelShortBuffer voxels)
        {
            Vec3 color = new Vec3();
            int hits = 0;
            float depth = 0;

            int axis = 2;
            int hitMode = 0;
            int depthMode = 1;

            switch (axis)
            {
                case 0:
                    for (int x = 1; x < voxels.width; x++)
                    {
                        float z = (float)pixel.X / (float)camera.width;
                        float y = (float)pixel.Y / (float)camera.height;
                        Voxel v = voxels.data[voxels.GetIndexFromPos(x, (int)(y * voxels.height), (int)(z * voxels.length))] ;

                        if(hitMode == 0)
                        {
                            if (v.r > 0 || v.b > 0 || v.g > 0)
                            {
                                if (hits == 0)
                                {
                                    depth = x;
                                }
                                color += new Vec3(v.r, v.g, v.b);
                                hits++;
                            }
                        }
                        else
                        {

                            if (v.isHit())
                            {
                                if (hits == 0)
                                {
                                    depth = x;
                                }
                                color += new Vec3(v.r, v.g, v.b);
                                hits++;
                            }
                        }

                    }
                    depth /= voxels.width;
                    break;
                case 1:
                    for (int y = 1; y < voxels.height; y++)
                    {
                        float x = (float)pixel.X / (float)camera.width;
                        float z = (float)pixel.Y / (float)camera.height;
                        Voxel v = voxels.data[voxels.GetIndexFromPos((int)(x * voxels.width), y, (int)(z * voxels.length))];

                        if (hitMode == 0)
                        {
                            if (v.r > 0 || v.b > 0 || v.g > 0)
                            {
                                if (hits == 0)
                                {
                                    depth = y;
                                }
                                color += new Vec3(v.r, v.g, v.b);
                                hits++;
                            }
                        }
                        else
                        {

                            if (v.isHit())
                            {
                                if (hits == 0)
                                {
                                    depth = y;
                                }
                                color += new Vec3(v.r, v.g, v.b);
                                hits++;
                            }
                        }

                    }
                    depth /= voxels.height;
                    break;
                case 2:
                    for (int z = 1; z < voxels.length; z++)
                    {
                        float x = (float)pixel.X / (float)camera.width;
                        float y = (float)pixel.Y / (float)camera.height;
                        Voxel v = voxels.data[voxels.GetIndexFromPos((int)(x * voxels.width), (int)(y * voxels.height), z)];

                        if (hitMode == 0)
                        {
                            if (v.r > 0 || v.b > 0 || v.g > 0)
                            {
                                if (hits == 0)
                                {
                                    depth = z;
                                }
                                color += new Vec3(v.r, v.g, v.b);
                                hits++;
                            }
                        }
                        else
                        {

                            if (v.isHit())
                            {
                                if (hits == 0)
                                {
                                    depth = z;
                                }
                                color += new Vec3(v.r, v.g, v.b);
                                hits++;
                            }
                        }

                    }
                    depth /= voxels.length;
                    break;

            }

            byte r = (byte)(depth * 255f);
            byte g = (byte)(depth * 255f);
            byte b = (byte)(depth * 255f);

            if(depthMode == 0)
            {
                r = (byte)(color.x / hits * 255f);
                g = (byte)(color.y / hits * 255f);
                b = (byte)(color.z / hits * 255f);
            }

            if (color.x > 255 || color.y > 255 || color.z > 255)
            {
                r = 255;
                g = 0;
                b = 255;
            }

            if (camera.isBGR)
            {
                output.writeFrameBuffer(pixel.X, pixel.Y, r, g, b);
            }
            else
            {
                output.writeFrameBuffer(pixel.X, pixel.Y, b, g, r);
            }
        }

    }
}
