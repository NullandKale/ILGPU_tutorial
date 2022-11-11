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
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using tutorial.GPU;
using tutorial.GPU.tutorial.GPU;

namespace tutorial
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        public Context context;
        public Accelerator device;

        public Action<Index2D, Camera, dPixelBuffer2D<byte>, dVoxelShortBuffer> generateFrameKernel;
        public Action<Index2D, Camera, FrameBuffer, dVoxelShortBuffer> fillVoxelsKernel;
        public Action<Index2D, dVoxelShortBuffer> clearVoxelsKernel;
        public Action<Index2D, Camera, bool, dPixelBuffer2D<byte>, FrameBuffer> generateTestKernel;
        public Action<Index2D, FrameBuffer, SpecializedValue<int>, SpecializedValue<int>> filterDepthMapKernel;

        public Camera camera;
        public VoxelShortBuffer voxelBuffer;
        public PixelBuffer2D<byte> frameBuffer;
        public Kinect kinect;

        public volatile bool shuttingDown = false;
        public volatile bool run = true;
        public volatile bool pause = false;

        public bool doFilter = true;

        public Thread renderThread;

        public int renderState = 2;

        public int ScreenWidth;
        public int ScreenHeight;

        public MainWindow()
        {
            InitializeComponent();
            InitGPU(false);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            onResolutionChanged((int)e.NewSize.Width, (int)e.NewSize.Height);
        }

        public void onResolutionChanged(int width, int height)
        {
            if(width > 0 && height > 0)
            {
                ScreenWidth = width;
                ScreenHeight = height;
            }

            if (renderThread != null)
            {
                run = false;
                renderThread.Join(1000);
            }

            if (frameBuffer != null)
            {
                frameBuffer.Dispose();
            }

            switch(renderState)
            {
                case 0:
                case 1:
                    width = kinect.Width;
                    height = kinect.Height;
                    break;
                case 2:
                    break;
            }

            if(device.IsDisposed)
            {
                Trace.WriteLine("WTF");
                return;
            }

            if(width == 0 || height == 0)
            {
                Trace.WriteLine("WTF");
                return;
            }

            frameBuffer = new PixelBuffer2D<byte>(device!, height, width);

            Vec3 origin = new Vec3(0, 0, voxelBuffer.GetDVoxelBuffer().aabb.max.z * 3);
            Vec3 lookAt = new Vec3(0, 0, voxelBuffer.GetDVoxelBuffer().aabb.max.z);

            //Vec3 origin = new Vec3(voxelBuffer.GetDVoxelBuffer().aabb.min.x * 3, 0, 0);
            //Vec3 lookAt = new Vec3(voxelBuffer.GetDVoxelBuffer().aabb.min.x, 0, 0);

            camera = new Camera(origin, lookAt, new Vec3(0, -1, 0), width, height, 110f, new Vec3(0, 0, 0));

            Application.Current?.Dispatcher.Invoke(() =>
            {
                frame.UpdateResolution(width, height);
                frame.update(device, frameBuffer);
            });

            Start();
        }

        public void InitGPU(bool forceCPU)
        {
            context = Context.Create(builder => builder.Cuda().CPU().EnableAlgorithms().Optimize(OptimizationLevel.O2));
            device = context.GetPreferredDevice(forceCPU).CreateAccelerator(context);
            kinect = new Kinect(device, optimizeForFace: true);

            voxelBuffer = new VoxelShortBuffer(device, kinect.Width, kinect.Height, 256, new Vec3(1, 1, 1));

            fillVoxelsKernel = device.LoadAutoGroupedStreamKernel<Index2D, Camera, FrameBuffer, dVoxelShortBuffer>(FillVoxelBuffer);
            clearVoxelsKernel = device.LoadAutoGroupedStreamKernel<Index2D, dVoxelShortBuffer>(ClearVoxelBuffer);
            generateFrameKernel = device.LoadAutoGroupedStreamKernel<Index2D, Camera, dPixelBuffer2D<byte>, dVoxelShortBuffer> (GenerateFrame);
            generateTestKernel = device.LoadAutoGroupedStreamKernel<Index2D, Camera, bool, dPixelBuffer2D<byte>, FrameBuffer> (GenerateTestFrame);
            filterDepthMapKernel = device.LoadAutoGroupedStreamKernel<Index2D, FrameBuffer, SpecializedValue<int>, SpecializedValue<int>>(FilterDepthMap);

        }

        private static void ClearVoxelBuffer(Index2D pixel, dVoxelShortBuffer voxels)
        {
            for(int z = 0; z < voxels.length; z++)
            {
                voxels.writeFrameBuffer(pixel.X, pixel.Y, z, default);
            }
        }

        private static void FilterDepthMap(Index2D pixel, FrameBuffer frameBuffer, SpecializedValue<int> filterDistance, SpecializedValue<int> passes)
        {
            for(int i = 0; i < passes; i++)
            {
                ushort filteredDepth = frameBuffer.FilterDepthPixel(pixel.X, pixel.Y, filterDistance, filterDistance, 2);
                frameBuffer.SetDepthPixel(pixel.X, pixel.Y, filteredDepth);
            }
        }

        private static void FillVoxelBuffer(Index2D pixel, Camera camera, FrameBuffer frameBuffer, dVoxelShortBuffer voxels)
        {
            float x = ((float)pixel.X / (float)frameBuffer.width);
            float y = ((float)pixel.Y / (float)frameBuffer.height);

            float depth = frameBuffer.GetDepthPixel(x,y);

            if (depth == 0)
            {
                depth = 1;
            }

            depth = 1.0f - depth;
            
            (byte r, byte g, byte b, byte a) colorData = frameBuffer.GetColorPixel(x,y);

            float extraThickness = 20;
            float z = depth * voxels.length;

            float endX = XMath.Max(z - extraThickness, 0);

            for (; z >= endX; z--)
            {
                voxels.writeFrameBuffer(x, y, z / voxels.length, colorData);
            }
        }

        private static void GenerateTestFrame(Index2D pixel, Camera camera, bool testDepth, dPixelBuffer2D<byte> output, FrameBuffer frameBuffer)
        {
            (byte r, byte g, byte b, byte a) color = default;

            if(testDepth)
            {
                float x = ((float)pixel.X / (float)frameBuffer.width);
                float y = ((float)pixel.Y / (float)frameBuffer.height);

                float depth = frameBuffer.GetDepthPixel(x, y);

                bool flipDepth = false;
                bool failed1 = false;
                bool failed2 = false;

                if (depth < 0)
                {
                    //failed1 = true;
                }
                else if (depth > camera.depthCutOff)
                {
                    failed2 = true;
                }

                if (flipDepth)
                {
                    depth = 1.0f - depth;
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


        private static void GenerateFrame(Index2D pixel, Camera camera, dPixelBuffer2D<byte> output, dVoxelShortBuffer voxels)
        {
            Ray ray = camera.GetRay(pixel.X, pixel.Y);
            ray = new Ray(ray.a, new Vec3(ray.b.x, ray.b.y, ray.b.z));
            Voxel hit = voxels.hit(ray, 0.001f, float.MaxValue);

            if (hit.r != 0 && hit.g != 0 && hit.b != 0)
            {
                if (camera.isBGR)
                {
                    output.writeFrameBuffer(pixel.X, pixel.Y, hit.r, hit.g, hit.b);
                }
                else
                {
                    output.writeFrameBuffer(pixel.X, pixel.Y, hit.b, hit.g, hit.r);
                }
            }
            else
            {
                output.writeFrameBuffer(pixel.X, pixel.Y, 1, 0, 1);
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Q)
            {
                camera = new Camera(camera, new Vec3(0, 0, -10), new Vec3());
            }

            if (e.Key == Key.E)
            {
                camera = new Camera(camera, new Vec3(0, 0, 10), new Vec3());
            }

            if (e.Key == Key.W)
            {
                camera = new Camera(camera, new Vec3(0, 10, 0), new Vec3());
            }

            if (e.Key == Key.S)
            {
                camera = new Camera(camera, new Vec3(0, -10, 0), new Vec3());
            }

            if (e.Key == Key.A)
            {
                camera = new Camera(camera, new Vec3(-10, 0, 0), new Vec3());
            }

            if (e.Key == Key.D)
            {
                camera = new Camera(camera, new Vec3(10, 0, 0), new Vec3());
            }

            if (e.Key == Key.Space)
            {
                TakeScreenshot();
            }

            if (e.Key == Key.D1)
            {
                if (renderState != 0)
                {
                    renderState = 0;
                    onResolutionChanged(0, 0);
                }
            }

            if (e.Key == Key.D2)
            {
                if (renderState != 1)
                {
                    renderState = 1;
                    onResolutionChanged(0, 0);
                }
            }

            if (e.Key == Key.D3)
            {
                if(renderState != 2)
                {
                    renderState = 2;
                    onResolutionChanged(ScreenWidth, ScreenHeight);
                }
            }

            if(e.Key == Key.T)
            {
                kinect.minMax.max = 1000;
            }

            if(e.Key == Key.Y)
            {
                kinect.minMax.max = (ushort)(ushort.MaxValue / 8.0);
            }

            if(e.Key == Key.F)
            {
                doFilter = !doFilter;
            }

            if (e.Key == Key.Up)
            {
                camera = new Camera(camera, camera.verticalFov + 5);
            }

            if (e.Key == Key.Down)
            {
                camera = new Camera(camera, camera.verticalFov - 5);
            }

            if(e.Key == Key.Left)
            {
                voxelBuffer.UpdateScale(voxelBuffer.scale - new Vec3(0, 0, 0.05f));
            }

            if (e.Key == Key.Right)
            {
                voxelBuffer.UpdateScale(voxelBuffer.scale + new Vec3(0, 0, 0.05f));
            }

        }

        private void TakeScreenshot()
        {
            pause = true;
            camera.isBGR = true;

            float movement = 25;
            int totalFrames = 75;

            camera = new Camera(camera, new Vec3(movement * totalFrames / 2f, 0, 0), new Vec3());

            for (int i = 0; i < totalFrames; i++)
            {
                camera = new Camera(camera, new Vec3(-movement, 0, 0), new Vec3());

                generateFrameKernel(new Index2D(frameBuffer.width - 1, frameBuffer.height - 1), camera, frameBuffer.GetDPixelBuffer(), voxelBuffer.GetDVoxelBuffer());
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

        private void Start()
        {
            run = true;
            renderThread = new Thread(updateRenderThread);
            renderThread.IsBackground = true;
            renderThread.Start();
        }

        private void updateRenderThread()
        {
            Stopwatch Timer = new Stopwatch();
            
            while(run)
            {
                if (pause)
                {
                    Thread.Sleep(250);
                }
                else
                {
                    Timer.Start();

                    FrameBuffer currentFrame = kinect.GetCurrentFrame();
                    currentFrame.locked = true;

                    currentFrame.filtered = false;
                    if(doFilter && !currentFrame.filtered)
                    {
                        filterDepthMapKernel(new Index2D(kinect.Width - 1, kinect.Height - 1), currentFrame, new SpecializedValue<int>(2), new SpecializedValue<int>(15));
                        //currentFrame.filtered = true;
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
                            clearVoxelsKernel(new Index2D(kinect.Width - 1, kinect.Height - 1), voxelBuffer.GetDVoxelBuffer());
                            fillVoxelsKernel(new Index2D(kinect.Width - 1, kinect.Height - 1), camera, currentFrame, voxelBuffer.GetDVoxelBuffer());
                            generateFrameKernel(new Index2D(frameBuffer.width - 1, frameBuffer.height - 1), camera, frameBuffer.GetDPixelBuffer(), voxelBuffer.GetDVoxelBuffer());
                            break;
                    }

                    try
                    {
                        device.Synchronize();
                    }
                    catch (Exception e)
                    {
                        Trace.WriteLine(e.ToString());
                    }

                    currentFrame.locked = false;

                    try
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            frame.frameTime = Timer.Elapsed.Milliseconds;
                            frame.update(device, frameBuffer);
                        });
                    }
                    catch
                    {
                        run = false;
                    }

                    Timer.Stop();

                }

                int timeToSleep = (int)(1000.0 / 60.0 - Timer.Elapsed.TotalMilliseconds);

                if (timeToSleep > 0)
                {
                    Thread.Sleep(timeToSleep);
                }

                Timer.Reset();
            }

            if(shuttingDown)
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            voxelBuffer.Dispose();
            frameBuffer.Dispose();

            kinect.Dispose();
            device.Dispose();
            context.Dispose();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            shuttingDown = true;
            run = false;
            Thread.Sleep(5000);
        }
    }
}
