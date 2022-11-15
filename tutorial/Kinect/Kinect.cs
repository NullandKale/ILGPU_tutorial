using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Algorithms.Sequencers;
using ILGPU.Runtime;
using ILGPU.Runtime.OpenCL;
using Microsoft.Azure.Kinect.Sensor;
using tutorial.GPU;
using tutorial.UI;
using Device = Microsoft.Azure.Kinect.Sensor.Device;
using Image = Microsoft.Azure.Kinect.Sensor.Image;

namespace tutorial.Kinect
{
    public class Kinect : IDisposable
    {
        Accelerator gpu;
        Device device;
        CameraCalibration current;
        Transformation transform;
        bool cameraSpace;

        const int bufferCount = 2;
        volatile int currentBuffer = 0;

        MemoryBuffer1D<byte, Stride1D.Dense>[] colorBuffers;
        MemoryBuffer1D<ushort, Stride1D.Dense>[] depthBuffers;
        FrameBuffer[] frameBuffers;

        public (ushort min, ushort max) minMax;

        public volatile bool run = true;
        public Thread thread;

        public Kinect(Accelerator gpu, bool optimizeForFace, bool cameraSpace, int kinect)
        {
            this.gpu = gpu;
            this.cameraSpace = cameraSpace;

            minMax = (500, (ushort)(optimizeForFace ? 1000 : ushort.MaxValue / 8.0));
            //minMax = (0, ushort.MaxValue);

            device = Device.Open(kinect);
            device.SetColorControl(ColorControlCommand.ExposureTimeAbsolute, ColorControlMode.Auto, 0);

            device.StartCameras(new DeviceConfiguration
            {
                CameraFPS = FPS.FPS5,
                ColorFormat = ImageFormat.ColorBGRA32,
                DepthMode = DepthMode.WFOV_2x2Binned,
                SynchronizedImagesOnly = true,
                WiredSyncMode = kinect == 0 ? WiredSyncMode.Master : WiredSyncMode.Subordinate,
                ColorResolution = ColorResolution.R720p,
            });

            Calibration calibration = device.GetCalibration();

            transform = calibration.CreateTransformation();
            current = cameraSpace ? calibration.ColorCameraCalibration : calibration.DepthCameraCalibration;

            colorBuffers = new MemoryBuffer1D<byte, Stride1D.Dense>[bufferCount];
            depthBuffers = new MemoryBuffer1D<ushort, Stride1D.Dense>[bufferCount];
            frameBuffers = new FrameBuffer[bufferCount];

            for (int i = 0; i < bufferCount; i++)
            {
                colorBuffers[i] = gpu.Allocate1D<byte>(current.ResolutionWidth * current.ResolutionHeight * 4);
                depthBuffers[i] = gpu.Allocate1D<ushort>(current.ResolutionHeight * current.ResolutionWidth);
                frameBuffers[i] = new FrameBuffer(current.ResolutionWidth, current.ResolutionHeight, minMax.min, minMax.max, colorBuffers[i], depthBuffers[i]);
                frameBuffers[i].locked = false;
            }

            thread = new Thread(() =>
            {
                Stopwatch timer = new Stopwatch();

                while (run)
                {
                    timer.Restart();

                    TryCaptureFromCamera();

                    timer.Stop();

                    RenderFrame.SetCaptureTime(timer.Elapsed.TotalMilliseconds);

                    int timeToSleep = (int)(1000.0 / 60.0 - timer.Elapsed.TotalMilliseconds);

                    if (timeToSleep > 0)
                    {
                        Thread.Sleep(timeToSleep);
                    }
                }
            });
            thread.IsBackground = true;
            thread.Start();

        }

        public int Width => current.ResolutionWidth;
        public int Height => current.ResolutionHeight;

        public void TryCaptureFromCamera()
        {
            try
            {
                int nextBuffer = (currentBuffer + 1) % bufferCount;

                while (frameBuffers[nextBuffer].locked)
                {
                    Trace.WriteLine("Capture thread out of frames");
                }
                frameBuffers[nextBuffer].filtered = false;
                CaptureFromCamera(nextBuffer);
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.ToString());
            }
        }

        private void CaptureFromCamera(int nextBuffer)
        {
            var colorBuffer = colorBuffers[nextBuffer];
            var depthBuffer = depthBuffers[nextBuffer];

            using Capture capture = device.GetCapture();

            unsafe
            {
                if (capture.Color != null)
                {
                    if(!cameraSpace)
                    {
                        using Image colorImage = transform.ColorImageToDepthCamera(capture);
                        using MemoryHandle c = colorImage.Memory.Pin();
                        Span<byte> colorData = new Span<byte>(c.Pointer, colorImage.Memory.Length);
                        colorBuffer.View.CopyFromCPU(gpu.DefaultStream, colorData);
                    }
                    else
                    {
                        using MemoryHandle c = capture.Color.Memory.Pin();
                        Span<byte> colorData = new Span<byte>(c.Pointer, capture.Color.Memory.Length);
                        colorBuffer.View.CopyFromCPU(gpu.DefaultStream, colorData);
                    }


                }

                if (capture.Depth != null)
                {
                    if (cameraSpace)
                    {
                        using Image depthImage = transform.DepthImageToColorCamera(capture.Depth);
                        using MemoryHandle d = depthImage.Memory.Pin();
                        Span<ushort> depthData = new Span<ushort>(d.Pointer, depthImage.Memory.Length / 2);
                        depthBuffer.View.CopyFromCPU(gpu.DefaultStream, depthData);
                    }
                    else
                    {
                        using MemoryHandle d = capture.Depth.Memory.Pin();
                        Span<ushort> depthData = new Span<ushort>(d.Pointer, capture.Depth.Memory.Length / 2);
                        depthBuffer.View.CopyFromCPU(gpu.DefaultStream, depthData);
                    }
                }
            }

            gpu.Synchronize();

            currentBuffer = nextBuffer;
        }

        public Bitmap BitmapFromCamera(bool doDepth = false)
        {
            using Capture capture = device.GetCapture();

            Bitmap bitmap = new Bitmap(capture.Color.WidthPixels, capture.Color.HeightPixels, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            var region = new Rectangle(0, 0, Width, Height);

            var lockedBitmap = bitmap.LockBits(region, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            unsafe
            {
                if (doDepth)
                {
                    if (capture.Depth != null)
                    {
                        if (cameraSpace)
                        {
                            using Image depthImage = transform.DepthImageToColorCamera(capture.Depth);
                            using MemoryHandle pin = depthImage.Memory.Pin();
                            Buffer.MemoryCopy(pin.Pointer, lockedBitmap.Scan0.ToPointer(), depthImage.Size, depthImage.Size);
                        }
                        else
                        {
                            using MemoryHandle pin = capture.Depth.Memory.Pin();
                            Buffer.MemoryCopy(pin.Pointer, lockedBitmap.Scan0.ToPointer(), capture.Depth.Size, capture.Depth.Size);
                        }

                    }
                }
                else
                {
                    if (capture.Color != null)
                    {
                        if(!cameraSpace)
                        {
                            //using Image colorImage = transform.ColorImageToDepthCamera(capture);
                            //using MemoryHandle pin = colorImage.Memory.Pin();
                            using var pin = capture.Color.Memory.Pin();
                            Buffer.MemoryCopy(pin.Pointer, lockedBitmap.Scan0.ToPointer(), capture.Color.Size, capture.Color.Size);
                        }
                        else
                        {
                            using Image colorImage = transform.ColorImageToDepthCamera(capture);
                            using MemoryHandle pin = colorImage.Memory.Pin();
                            Buffer.MemoryCopy(pin.Pointer, lockedBitmap.Scan0.ToPointer(), colorImage.Size, colorImage.Size);
                        }
                    }
                }
            }

            bitmap.UnlockBits(lockedBitmap);
            return bitmap;
        }

        public ref FrameBuffer GetCurrentFrame()
        {
            frameBuffers[currentBuffer].minDepth = minMax.min;
            frameBuffers[currentBuffer].maxDepth = minMax.max;
            return ref frameBuffers[currentBuffer];
        }

        public void Dispose()
        {
            run = false;
            thread.Join();

            for (int i = 0; i < bufferCount; i++)
            {
                colorBuffers[i].Dispose();
                depthBuffers[i].Dispose();
            }

            device.StopCameras();
        }
    }
}
