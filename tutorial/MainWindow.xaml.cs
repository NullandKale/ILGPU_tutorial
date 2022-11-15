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
using tutorial.Kinect;

namespace tutorial
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        public Renderer renderer;

        public Vec3 offset = new Vec3();
        public int depth = 0;
        public float min = 0;
        public float max = 1000;

        public float scale = -2;
        public int ScreenWidth;
        public int ScreenHeight;

        public MainWindow()
        {
            InitializeComponent();
            renderer = new Renderer(this, false);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            onResolutionChanged((int)e.NewSize.Width, (int)e.NewSize.Height);
        }

        public void onResolutionChanged(int width, int height)
        {
            if (scale > 0)
            {
                height = (int)(height * scale);
                width = (int)(width * scale);
            }
            else
            {
                height = (int)(height / -scale);
                width = (int)(width / -scale);
            }

            if (width > 0 && height > 0)
            {
                ScreenWidth = width;
                ScreenHeight = height;
            }

            renderer.Start(width, height);
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Q)
            {
                renderer.camera = new Camera(renderer.camera, new Vec3(0, 0, -10), new Vec3());
            }

            if (e.Key == Key.E)
            {
                renderer.camera = new Camera(renderer.camera, new Vec3(0, 0, 10), new Vec3());
            }

            if (e.Key == Key.W)
            {
                renderer.camera = new Camera(renderer.camera, new Vec3(0, 10, 0), new Vec3());
            }

            if (e.Key == Key.S)
            {
                renderer.camera = new Camera(renderer.camera, new Vec3(0, -10, 0), new Vec3());
            }

            if (e.Key == Key.A)
            {
                renderer.camera = new Camera(renderer.camera, new Vec3(-10, 0, 0), new Vec3());
            }

            if (e.Key == Key.D)
            {
                renderer.camera = new Camera(renderer.camera, new Vec3(10, 0, 0), new Vec3());
            }

            if (e.Key == Key.Space)
            {
                renderer.TakePhotoset();
            }

            if (e.Key == Key.D1)
            {
                if (renderer.renderState != 0)
                {
                    renderer.renderState = 0;
                    onResolutionChanged(0, 0);
                }
            }

            if (e.Key == Key.D2)
            {
                if (renderer.renderState != 1)
                {
                    renderer.renderState = 1;
                    onResolutionChanged(0, 0);
                }
            }

            if (e.Key == Key.D3)
            {
                if(renderer.renderState != 2)
                {
                    renderer.renderState = 2;
                    onResolutionChanged(ScreenWidth, ScreenHeight);
                }
            }

            if(e.Key == Key.T)
            {
                renderer.kinect = 0;
            }

            if (e.Key == Key.Y)
            {
                renderer.kinect = 1;
            }

            if (e.Key == Key.F)
            {
                renderer.doFilter = !renderer.doFilter;
            }

            if (e.Key == Key.Up)
            {
                renderer.camera = new Camera(renderer.camera, renderer.camera.verticalFov + 5);
            }

            if (e.Key == Key.Down)
            {
                renderer.camera = new Camera(renderer.camera, renderer.camera.verticalFov - 5);
            }

            if(e.Key == Key.Left)
            {
                renderer.voxelBuffer.UpdateScale(renderer.voxelBuffer.scale - new Vec3(0, 0, 0.05f));
            }

            if (e.Key == Key.Right)
            {
                renderer.voxelBuffer.UpdateScale(renderer.voxelBuffer.scale + new Vec3(0, 0, 0.05f));
            }

        }

        private void Window_Closed(object sender, EventArgs e)
        {
            renderer.shuttingDown = true;
            renderer.run = false;
            Thread.Sleep(5000);
        }

        public void Dispose()
        {
            renderer.Dispose();
        }

        private void camera0XSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            offset.x = (float)e.NewValue;
        }

        private void camera0YSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            offset.y = (float)e.NewValue;
        }
        private void camera0ZSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            offset.z = (float)e.NewValue;
        }

        private void camera0WSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            depth = (int)e.NewValue;
        }

        private void camera0ASlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            min = (float)e.NewValue;
            if (renderer != null)
            {
                renderer.kinectManager.UpdateMinMax((ushort)min, (ushort)max);
            }
        }

        private void camera0BSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            max = (int)e.NewValue;
            if (renderer != null)
            {
                renderer.kinectManager.UpdateMinMax((ushort)min, (ushort)max);
            }
        }
    }
}
