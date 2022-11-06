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

using Device = Microsoft.Azure.Kinect.Sensor.Device;
using Image = Microsoft.Azure.Kinect.Sensor.Image;

namespace tutorial
{
    public class Kinect : IDisposable
    {
        Accelerator gpu;
        Device device;
        CameraCalibration color;
        CameraCalibration depth;
        CameraCalibration current;
        Transformation transform;

        MemoryBuffer1D<byte, Stride1D.Dense> colorBuffer;
        MemoryBuffer1D<UInt16, Stride1D.Dense> depthBuffer;

        public Kinect(Accelerator gpu)
        {
            this.gpu = gpu;

            device = Device.Open();
            device.SetColorControl(ColorControlCommand.ExposureTimeAbsolute, ColorControlMode.Auto, 0);

            device.StartCameras(new DeviceConfiguration
            {
                CameraFPS = FPS.FPS15,
                ColorFormat = ImageFormat.ColorBGRA32,
                DepthMode = DepthMode.WFOV_Unbinned,
                SynchronizedImagesOnly = true,
                ColorResolution = ColorResolution.R720p
            });

            Calibration calibration = device.GetCalibration();
            
            transform = calibration.CreateTransformation();
            color = calibration.ColorCameraCalibration;
            depth = calibration.DepthCameraCalibration;
            current = color;

            colorBuffer = gpu.Allocate1D<byte>(current.ResolutionWidth * current.ResolutionHeight * 4);
            depthBuffer = gpu.Allocate1D<UInt16>(current.ResolutionHeight * current.ResolutionWidth);
        }

        public int Width => current.ResolutionWidth;
        public int Height => current.ResolutionHeight;

        public void TryCaptureFromCamera()
        {
            try
            {
                CaptureFromCamera();
            }
            catch(Exception e) 
            {
                Trace.WriteLine(e.ToString());
            }
        }

        public void CaptureFromCamera()
        {
            using (Capture capture = device.GetCapture())
            {
                unsafe 
                { 
                    Image colorImage = capture.Color;

                    if (colorImage != null)
                    {
                        if(current == depth)
                        {
                            colorImage = transform.ColorImageToDepthCamera(capture);
                        }

                        using (var c = colorImage.Memory.Pin())
                        {
                            Span<byte> colorData = new Span<byte>(c.Pointer, colorImage.Memory.Length);

                            if (colorData.Length == colorBuffer.Length)
                            {
                                colorBuffer.View.CopyFromCPU(gpu.DefaultStream, colorData);
                            }
                            else
                            {
                                Trace.WriteLine("WTF");
                            }
                        }

                        colorImage.Dispose();
                    }

                    Image depthImage = capture.Depth;

                    if (depthImage != null)
                    {
                        if (current == color)
                        {
                            depthImage = transform.DepthImageToColorCamera(capture.Depth);
                        }

                        using (var d = depthImage.Memory.Pin())
                        {
                            Span<UInt16> depthData = new Span<UInt16>(d.Pointer, depthImage.Memory.Length / 2);

                            if (depthData.Length == depthBuffer.Length)
                            {
                                depthBuffer.View.CopyFromCPU(gpu.DefaultStream, depthData);
                            }
                            else
                            {
                                Trace.WriteLine("WTF");
                            }
                        }

                        depthImage.Dispose();
                    }
                }
            }
        }

        public readonly struct MinUInt16T : IScanReduceOperation<ushort>
        {
            public string CLCommand => "<#= op.Name.ToLower() #>";
            public ushort Identity => 0;
            public ushort Apply(ushort first, ushort second) => (XMath.Min(first, second));
            public void AtomicApply(ref ushort target, ushort value) =>
                Atomic.Exchange(ref Unsafe.As<ushort, int>(ref target), value);
        }

        public readonly struct MaxUInt16T : IScanReduceOperation<ushort>
        {
            public string CLCommand => "<#= op.Name.ToLower() #>";
            public ushort Identity => 0;
            public ushort Apply(ushort first, ushort second) => (XMath.Max(first, second));
            public void AtomicApply(ref ushort target, ushort value)
            {

                Atomic.Exchange(ref Unsafe.As<ushort, int>(ref target), value);
            }
        }

        public (ushort min, ushort max) CalculateMinMaxDepth()
        {
            //return (0, ushort.MaxValue);

            using var minDepth = gpu.Allocate1D<ushort>(1);
            using var maxDepth = gpu.Allocate1D<ushort>(1);

            gpu.Sequence(gpu.DefaultStream, depthBuffer.View, new UInt16Sequencer());
            gpu.Reduce<UInt16, MinUInt16T>(
                gpu.DefaultStream,
                depthBuffer.View,
                minDepth.View);

            gpu.Sequence(gpu.DefaultStream, depthBuffer.View, new UInt16Sequencer());
            gpu.Reduce<UInt16, MaxUInt16T>(
                gpu.DefaultStream,
                depthBuffer.View,
                maxDepth.View);

            gpu.Synchronize();

            ushort[] min = new ushort[1];
            minDepth.CopyToCPU(min);

            ushort[] max = new ushort[1];
            maxDepth.CopyToCPU(max);

            return (min[0], max[0]); 
        }

        public FrameBuffer GetCurrentFrame()
        {
            var minMax = CalculateMinMaxDepth();

            return new FrameBuffer(current.ResolutionWidth, current.ResolutionHeight,
                                   current.ResolutionWidth, current.ResolutionHeight,
                                   minMax.min, minMax.max,
                                   colorBuffer, depthBuffer);
        }

        public void Dispose()
        {
            colorBuffer.Dispose();
            depthBuffer.Dispose();

            device.StopCameras();
        }
    }

    public struct FrameBuffer
    {
        public ushort minDepth;
        public ushort maxDepth;
        public int colorWidth;
        public int colorHeight;
        public int depthWidth;
        public int depthHeight;

        public ArrayView1D<byte, Stride1D.Dense> color;
        public ArrayView1D<UInt16, Stride1D.Dense> depth;

        public FrameBuffer(int colorWidth, int colorHeight,
                           int depthWidth, int depthHeight,
                           ushort minDepth, ushort maxDepth,
                           ArrayView1D<byte, Stride1D.Dense> color, ArrayView1D<UInt16, Stride1D.Dense> depth)
        {
            this.minDepth = minDepth;
            this.maxDepth = maxDepth;
            this.colorWidth = colorWidth;
            this.colorHeight = colorHeight;
            this.depthWidth = depthWidth;
            this.depthHeight = depthHeight;
            this.color = color;
            this.depth = depth;
        }

        public (byte r, byte g, byte b, byte a) GetColorPixel(float x, float y)
        {
            return GetColorPixel((int)(x * colorWidth), (int)(y * colorHeight));
        }

        public (byte r, byte g, byte b, byte a) GetColorPixel(int x, int y)
        {
            return GetColorPixel(y * colorWidth + x);
        }

        public (byte r, byte g, byte b, byte a) GetColorPixel(int index)
        {
            index *= 4;

            if(index >= 0 && index < color.Length)
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

        public static float Remap(float value, float currentMin, float currentMax, float newMin, float newMax)
        {
            return (value - currentMin) / (currentMax - currentMin) * (newMax - newMin) + newMin;
        }

        public float GetDepthPixel(float x, float y)
        {
            float xCord = x * depthWidth;
            float yCord = y * depthHeight;

            ushort depth = GetDepthPixel((int) xCord, (int)yCord);
            float toReturn = Remap(depth, minDepth, maxDepth, 0, 1);
            //toReturn = 1.0f - toReturn;

            return toReturn;
        }

        public UInt16 GetDepthPixel(int x, int y)
        {
            return GetDepthPixel(y * depthWidth + x);
        }

        public UInt16 GetDepthPixel(int index)
        {
            if (index >= 0 && index < depth.Length)
            {
                return depth[index];
            }
            else
            {
                return ushort.MaxValue;
            }
        }
    }
}
