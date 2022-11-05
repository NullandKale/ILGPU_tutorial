using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using Microsoft.Azure.Kinect.Sensor;
using tutorial.GPU;

using Device = Microsoft.Azure.Kinect.Sensor.Device;
using Image = Microsoft.Azure.Kinect.Sensor.Image;

namespace tutorial
{
    public class Kinect : IDisposable
    {
        Device device;
        CameraCalibration color;
        CameraCalibration depth;

        const int bufferCount = 2;
        int currentBuffer = 0;
        MemoryBuffer1D<byte, Stride1D.Dense>[] colorBuffers;
        MemoryBuffer1D<short, Stride1D.Dense>[] depthBuffers;

        public Kinect(Accelerator gpu)
        {
            device = Device.Open();

            device.SetColorControl(ColorControlCommand.ExposureTimeAbsolute, ColorControlMode.Auto, 0);

            device.StartCameras(new DeviceConfiguration
            {
                CameraFPS = FPS.FPS15,
                ColorFormat = ImageFormat.ColorBGRA32,
                DepthMode = DepthMode.WFOV_2x2Binned,
                SynchronizedImagesOnly = true,
                ColorResolution = ColorResolution.R720p
            });

            Calibration calibration = device.GetCalibration();

            color = calibration.ColorCameraCalibration;
            depth = calibration.DepthCameraCalibration;

            colorBuffers = new MemoryBuffer1D<byte, Stride1D.Dense>[bufferCount];
            depthBuffers = new MemoryBuffer1D<short, Stride1D.Dense>[bufferCount];

            for(int i = 0; i < bufferCount; i++)
            {
                colorBuffers[i] = gpu.Allocate1D<byte>(color.ResolutionWidth * color.ResolutionHeight * 4);
                depthBuffers[i] = gpu.Allocate1D<short>(depth.ResolutionHeight * depth.ResolutionWidth);
            }

        }

        public void CaptureFromCamera()
        {
            int nextBuffer = (currentBuffer + 1) % bufferCount;

            using (Capture capture = device.GetCapture())
            {
                Image colorImage = capture.Color;
                Image depthImage = capture.Depth;

                unsafe
                {
                    Span<byte> colorData = colorImage.Memory.Span;
                    Span<short> depthData = MemoryMarshal.Cast<byte, short>(depthImage.Memory.Span);

                    colorBuffers[nextBuffer].CopyFromCPU(colorData.ToArray()); // this is slow and bad
                    depthBuffers[nextBuffer].CopyFromCPU(depthData.ToArray()); // this is slow and bad
                }
            }

            Interlocked.Exchange(ref currentBuffer, nextBuffer);
        }

        public FrameBuffer GetCurrentFrame()
        {
            return new FrameBuffer(color.ResolutionWidth, color.ResolutionHeight,
                                   depth.ResolutionWidth, depth.ResolutionHeight,
                                   colorBuffers[currentBuffer], depthBuffers[currentBuffer]);
        }

        public void Dispose()
        {
            for (int i = 0; i < bufferCount; i++)
            {
                colorBuffers[i].Dispose();
                depthBuffers[i].Dispose();
            }


        }
    }

    public struct FrameBuffer
    {
        public int colorWidth;
        public int colorHeight;
        public int depthWidth;
        public int depthHeight;

        public ArrayView1D<byte, Stride1D.Dense> color;
        public ArrayView1D<short, Stride1D.Dense> depth;

        public FrameBuffer(int colorWidth, int colorHeight,
                           int depthWidth, int depthHeight,
                           ArrayView1D<byte, Stride1D.Dense> color, ArrayView1D<short, Stride1D.Dense> depth)
        {
            this.colorWidth = colorWidth;
            this.colorHeight = colorHeight;
            this.depthWidth = depthWidth;
            this.depthHeight = depthHeight;
            this.color = color;
            this.depth = depth;
        }

        public (byte r, byte g, byte b, byte a) GetColorPixel(int x, int y)
        {
            return GetColorPixel(y * colorWidth + x);
        }

        public (byte r, byte g, byte b, byte a) GetColorPixel(int index)
        {
            index *= 4;

            return (color[index],
                color[index + 1],
                color[index + 2],
                color[index + 3]);
        }

        public short GetDepthPixel(int x, int y)
        {
            return GetDepthPixel(y * depthWidth + x);
        }

        public short GetDepthPixel(int index)
        {
            return depth[index];
        }
    }
}
