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
    public partial class MainWindow : Window
    {
        public Context context;
        public Accelerator device;

        public Action<Index2D, Camera, dPixelBuffer2D<byte>, dVoxelShortBuffer> generateFrameKernel;
        public Action<Index1D, dPixelBuffer2D<byte>, dPixelBuffer2D<byte>, dVoxelShortBuffer> fillVoxelsKernel;

        public Camera camera;
        public VoxelShortBuffer voxelBuffer;
        public GPUImageRGBD inputImage;
        public PixelBuffer2D<byte> frameBuffer;

        public volatile bool run = true;
        public volatile bool pause = false;

        public Thread renderThread;

        public MainWindow()
        {
            InitializeComponent();

            InitGPU(false);

            frame.onResolutionChanged = onResolutionChanged;
        }

        public void onResolutionChanged(int width, int height)
        {
            if (renderThread != null)
            {
                run = false;
                renderThread.Join(100);
            }

            if (frameBuffer != null)
            {
                frameBuffer.Dispose();
            }

            frameBuffer = new PixelBuffer2D<byte>(device!, height, width);
            camera = new Camera(new Vec3(0, 0, voxelBuffer.GetDVoxelBuffer().aabb.max.z * 5), new Vec3(0, 0, voxelBuffer.GetDVoxelBuffer().aabb.max.z), new Vec3(0, -1, 0), width, height, 110f, new Vec3(0, 0, 0));

            Application.Current?.Dispatcher.Invoke(() =>
            {
                frame.update(ref frameBuffer.GetRawFrameData());
            });

            Start();
        }

        public void InitGPU(bool forceCPU)
        {
            context = Context.Create(builder => builder.Cuda().CPU().EnableAlgorithms().Optimize(OptimizationLevel.O2));
            device = context.GetPreferredDevice(forceCPU).CreateAccelerator(context);

            //inputImage = new GPUImageRGBD(device, "C:\\Repos\\tmp\\spiderman pointing.png");
            //inputImage = new GPUImageRGBD(device, "C:\\Users\\zinsl\\Downloads\\birthday-rgbd.jpg");
            inputImage = new GPUImageRGBD(device, "C:\\Users\\zinsl\\Desktop\\Voxel_Depth_Test\\Nikki_RGBD.jpg");

            voxelBuffer = new VoxelShortBuffer(device, inputImage.image.width, inputImage.image.height, 256, new Vec3(1, 1, 0.85f));

            fillVoxelsKernel = device.LoadAutoGroupedStreamKernel<Index1D, dPixelBuffer2D<byte>, dPixelBuffer2D<byte>, dVoxelShortBuffer>(FillVoxelBuffer);
            generateFrameKernel = device.LoadAutoGroupedStreamKernel<Index2D, Camera, dPixelBuffer2D<byte>, dVoxelShortBuffer> (GenerateFrame);

        }

        private static void FillVoxelBuffer(Index1D pixel, dPixelBuffer2D<byte> depth, dPixelBuffer2D<byte> image, dVoxelShortBuffer voxels)
        {
            (byte x, byte y, byte z) depthData = depth.readFrameBuffer(pixel);
            (byte x, byte y, byte z) colorData = image.readFrameBuffer(pixel);

            (int x, int y) pos = depth.GetPosFromIndex(pixel);

            int extraThickness = 1;
            int z = (depthData.x + depthData.y + depthData.z) / 3;
            int endX = XMath.Max(z - extraThickness, 0);

            for (; z >= endX; z--)
            {
                voxels.writeFrameBuffer(pos.x, pos.y, z, colorData);
            }
        }


        private static void GenerateFrame(Index2D pixel, Camera camera, dPixelBuffer2D<byte> output, dVoxelShortBuffer voxels)
        {
            Ray ray = camera.GetRay(pixel.X, pixel.Y);
            ray = new Ray(ray.a, new Vec3(ray.b.x, ray.b.y, ray.b.z));
            (byte x, byte y, byte z) color;
            (byte x, byte y, byte z) hit = voxels.hit(ray, 0.001f, float.MaxValue);

            if (hit.x != 0 && hit.y != 0 && hit.z != 0)
            {
                if (camera.isBGR)
                {
                    output.writeFrameBufferFlipped(pixel.X, pixel.Y, hit.x, hit.y, hit.z);
                }
                else
                {
                    output.writeFrameBufferFlipped(pixel.X, pixel.Y, hit.z, hit.y, hit.x);
                }
            }
            else
            {
                output.writeFrameBufferFlipped(pixel.X, pixel.Y, 0, 0, 0);
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
                pause = true;
                camera.isBGR = true;
                for (int i = 0; i < 75; i++)
                {
                    camera = new Camera(camera, new Vec3(-25, 0, 0), new Vec3());

                    generateFrameKernel(new Index2D(frameBuffer.width - 1, frame.height - 1), camera, frameBuffer.GetDPixelBuffer(), voxelBuffer.GetDVoxelBuffer());
                    device.Synchronize();
                    if(i < 10)
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

            if(e.Key == Key.Up)
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

            fillVoxelsKernel(inputImage.image.width * inputImage.image.height, inputImage.depth.GetDPixelBuffer(), inputImage.image.GetDPixelBuffer(), voxelBuffer.GetDVoxelBuffer());

            while(run)
            {
                if (pause)
                {
                    Thread.Sleep(250);
                }
                else
                {
                    Timer.Start();

                    generateFrameKernel(new Index2D(frameBuffer.width - 1, frame.height - 1), camera, frameBuffer.GetDPixelBuffer(), voxelBuffer.GetDVoxelBuffer());
                    device.Synchronize();

                    Timer.Stop();

                    try
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            frame.frameTime = Timer.Elapsed.Milliseconds;
                            frame.update(ref frameBuffer.GetRawFrameData());
                        });
                    }
                    catch
                    {
                        run = false;
                    }

                    Timer.Reset();
                }

                Thread.Sleep(1);
            }
        }
    }
}
