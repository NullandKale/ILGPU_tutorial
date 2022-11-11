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

namespace tutorial
{
    public class Kinect : IDisposable
    {
        Accelerator gpu;
        Device device;
        CameraCalibration current;
        Transformation transform;

        const int bufferCount = 2;
        volatile int currentBuffer = 0;

        MemoryBuffer1D<byte, Stride1D.Dense>[] colorBuffers;
        MemoryBuffer1D<ushort, Stride1D.Dense>[] depthBuffers;
        FrameBuffer[] frameBuffers;

        public (ushort min, ushort max) minMax;

        public volatile bool run = true;
        public Thread thread;

        public Kinect(Accelerator gpu, bool optimizeForFace)
        {
            this.gpu = gpu;

            minMax = (0, (ushort)(optimizeForFace ? 1000 : (ushort.MaxValue / 8.0)));

            device = Device.Open();
            device.SetColorControl(ColorControlCommand.ExposureTimeAbsolute, ColorControlMode.Auto, 0);

            device.StartCameras(new DeviceConfiguration
            {
                CameraFPS = FPS.FPS30,
                ColorFormat = ImageFormat.ColorBGRA32,
                DepthMode = DepthMode.WFOV_2x2Binned,
                SynchronizedImagesOnly = false,
                ColorResolution = ColorResolution.R720p,
            });

            Calibration calibration = device.GetCalibration();
            
            transform = calibration.CreateTransformation();
            current = calibration.ColorCameraCalibration;

            colorBuffers = new MemoryBuffer1D<byte, Stride1D.Dense>[bufferCount];
            depthBuffers = new MemoryBuffer1D<ushort, Stride1D.Dense>[bufferCount];
            frameBuffers = new FrameBuffer[bufferCount];

            for(int i = 0; i < bufferCount; i++)
            {
                colorBuffers[i] = gpu.Allocate1D<byte>(current.ResolutionWidth * current.ResolutionHeight * 4);
                depthBuffers[i] = gpu.Allocate1D<ushort>(current.ResolutionHeight * current.ResolutionWidth);
                frameBuffers[i] = new FrameBuffer(current.ResolutionWidth, current.ResolutionHeight, minMax.min, minMax.max, colorBuffers[i], depthBuffers[i]);
                frameBuffers[i].locked = false;
            }

            thread = new Thread(() => 
            {
                Stopwatch timer = new Stopwatch();

                while(run)
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
                var frameBuffer = frameBuffers[nextBuffer];

                while(frameBuffer.locked)
                {
                    Trace.WriteLine("Capture thread out of frames");
                }
                //frameBuffer.filtered = false;
                CaptureFromCamera(nextBuffer);
            }
            catch(Exception e) 
            {
                Trace.WriteLine(e.ToString());
            }
        }

        private void CaptureFromCamera(int nextBuffer)
        {
            var colorBuffer = colorBuffers[nextBuffer];
            var depthBuffer = depthBuffers[nextBuffer];

            using (Capture capture = device.GetCapture())
            {
                unsafe 
                { 
                    if (capture.Color != null)
                    {
                        using (var c = capture.Color.Memory.Pin())
                        {
                            Span<byte> colorData = new Span<byte>(c.Pointer, capture.Color.Memory.Length);
                            colorBuffer.View.CopyFromCPU(gpu.DefaultStream, colorData);
                        }
                    }

                    if (capture.Depth != null)
                    {
                        using (Image depthImage = transform.DepthImageToColorCamera(capture.Depth))
                        {
                            using (var d = depthImage.Memory.Pin())
                            {
                                Span<ushort> depthData = new Span<ushort>(d.Pointer, depthImage.Memory.Length / 2);
                                depthBuffer.View.CopyFromCPU(gpu.DefaultStream, depthData);
                            }
                        }
                    }
                }
            }

            currentBuffer = nextBuffer;
        }

        public FrameBuffer GetCurrentFrame()
        {
            frameBuffers[currentBuffer].minDepth = minMax.min;
            frameBuffers[currentBuffer].maxDepth = minMax.max;
            return frameBuffers[currentBuffer];
        }

        public void Dispose()
        {
            run = false;
            thread.Join(5000);

            for (int i = 0; i < bufferCount; i++)
            {
                colorBuffers[i].Dispose();
                depthBuffers[i].Dispose();
            }

            device.StopCameras();
        }
    }
}
