using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tutorial.GPU;

namespace tutorial.Kinect
{
    public class KinectManager : IDisposable
    {
        private int kinectCount = 2;
        private Kinect[] kinects;

        public int Width => kinects[0].Width;
        public int Height => kinects[0].Height;

        public KinectManager(ILGPU.Runtime.Accelerator gpu)
        {
            if (kinectCount > Device.GetInstalledCount())
            {
                kinectCount = Device.GetInstalledCount();
            }

            kinects = new Kinect[kinectCount];

            for (int i = 0; i < kinectCount; i++)
            {
                kinects[i] = new Kinect(gpu, true, true, i);
            }

            //TakeScreenshot();
        }

        public void TakeScreenshot()
        {
            for (int i = 0; i < kinectCount; i++)
            {
                Bitmap b = kinects[i].BitmapFromCamera();
                b.Dispose();
            }

            for (int j = 0; j < 1; j++)
            {
                for (int i = 0; i < kinectCount; i++)
                {
                    Bitmap b = kinects[i].BitmapFromCamera();
                    b.Save($"kinect_{(i + (j * 2)).ToString("D2")}.png", System.Drawing.Imaging.ImageFormat.Png);
                    b.Dispose();
                }
            }

            Environment.Exit(0);
        }

        public void UpdateMinMax(ushort min, ushort max)
        {
            for (int i = 0; i < kinectCount; i++)
            {
                kinects[i].minMax = (min, max);
            }
        }


        public ref FrameBuffer GetFrameBuffer(int camera)
        {
            return ref kinects[camera].GetCurrentFrame();
        }

        public void Dispose()
        {
            for (int i = 0; i < kinectCount; i++)
            {
                kinects[i].Dispose();
            }
        }
    }
}
