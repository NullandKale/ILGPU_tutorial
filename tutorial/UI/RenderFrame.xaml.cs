using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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

namespace tutorial.UI
{
    /// <summary>
    /// Interaction logic for RenderFrame.xaml
    /// </summary>
    public partial class RenderFrame : UserControl
    {
        public double scale = -5;

        public int width;
        public int height;

        public WriteableBitmap? wBitmap;
        public Int32Rect rect;

        public Action<int, int>? onResolutionChanged;
        public double frameTime;

        public RenderFrame()
        {
            InitializeComponent();

            SizeChanged += RenderFrame_SizeChanged;
        }

        private void RenderFrame_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            width = (int)e.NewSize.Width;
            height = (int)e.NewSize.Height;
            UpdateResolution();
        }

        private void UpdateResolution()
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

            width += ((width * 3) % 4);

            wBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Rgb24, null);
            Frame.Source = wBitmap;
            rect = new Int32Rect(0, 0, width, height);
            onResolutionChanged?.Invoke(width, height);
        }

        public void update(ref byte[] data)
        {
            if (wBitmap != null)
            {
                if (data.Length == wBitmap.PixelWidth * wBitmap.PixelHeight * 3)
                {
                    wBitmap.Lock();
                    IntPtr pBackBuffer = wBitmap.BackBuffer;
                    Marshal.Copy(data, 0, pBackBuffer, data.Length);
                    wBitmap.AddDirtyRect(rect);
                    wBitmap.Unlock();

                    Info.Content = (int)frameTime + " MS";
                }
            }
        }
    }
}
