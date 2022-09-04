using ILGPU;
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

namespace tutorial
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Context context;
        public Accelerator device;

        public Action<Index1D, dPixelBuffer2D<byte>> generateFrameKernel;

        public VoxelBuffer<(short x, short y)> voxelBuffer;
        public PixelBuffer2D<byte> inputFrameBuffer;
        public PixelBuffer2D<byte> frameBuffer;

        public volatile bool run = true;
        public Thread renderThread;

        public MainWindow()
        {
            InitializeComponent();

            InitGPU(false);
            frame.onResolutionChanged = (int width, int height) =>
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

                FillRandom();
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    frame.update(ref frameBuffer.GetRawFrameData());
                });
            };
        }

        private void FillRandom()
        {
            Random random = new Random();

            Parallel.For(0, frameBuffer.width * frameBuffer.height, (int i) =>
            {
                byte isAlive = (byte)((random.Next() % 9 == 0) ? 255 : 0);
                frameBuffer[i * 3] = isAlive;
                frameBuffer[i * 3 + 1] = isAlive;
                frameBuffer[i * 3 + 2] = isAlive;
            });
        }

        public void InitGPU(bool forceCPU)
        {
            context = Context.Create(builder => builder.Cuda().CPU().EnableAlgorithms());
            device = context.GetPreferredDevice(forceCPU).CreateAccelerator(context);

            generateFrameKernel = device.LoadAutoGroupedStreamKernel<Index1D, dPixelBuffer2D<byte>> (GenerateFrame);
        }

        private static void GenerateFrame(Index1D pixel, dPixelBuffer2D<byte> output)
        {
            int neighbors = 0;
            bool isAlive = false;

            (int x, int y) pos = output.GetPosFromIndex(pixel);

            if (pos.x < 1 || pos.y < 1 || pos.x >= output.width - 1 || pos.y >= output.height - 1)
            {
                return;
            }

            for(int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++)
                {
                    if(output.readFrameBuffer(pos.x - i, pos.y - j).x > 0)
                    {
                        if (i == 0 && j == 0)
                        {
                            isAlive = true;
                        }
                        else
                        {
                            neighbors++;
                        }
                    }
                }
            }

            if(!isAlive && neighbors == 3)
            {
                output.writeFrameBuffer(pixel, 255, 255, 255);
            }
            else if(isAlive && (neighbors > 2 || neighbors < 3))
            {
                output.writeFrameBuffer(pixel, 0, 0, 0);
            }
            else
            {
                output.writeFrameBuffer(pixel, 0, 0, 0);
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Space)
            {
                Start();
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

            while(run)
            {
                Timer.Start();

                generateFrameKernel(frameBuffer.width * frameBuffer.height, frameBuffer.GetDPixelBuffer());
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

                Thread.Sleep(250); 

            }
        }
    }
}
