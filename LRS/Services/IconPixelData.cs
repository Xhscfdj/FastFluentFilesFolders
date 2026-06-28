//using System;
//using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
//using System.Linq;
using System.Runtime.InteropServices;
//using System.Text;
//using System.Threading.Tasks;

namespace LRS.Services
{
	public class IconPixelData
	{
		public int Width { get; }
		public int Height { get; }
		public byte[] Pixels { get; } // BGRA 格式
		public IconPixelData(int width, int height, byte[] pixels)
		{
			Width = width;
			Height = height;
			Pixels = pixels;
		}
		public static IconPixelData ExtractIconPixelData(Icon icon)
		{
			using (var bitmap = icon.ToBitmap())
			{
				int width = bitmap.Width;
				int height = bitmap.Height;
				var bmpData = bitmap.LockBits(
					new System.Drawing.Rectangle(0, 0, width, height),
					ImageLockMode.ReadOnly,
					System.Drawing.Imaging.PixelFormat.Format32bppArgb);

				try
				{
					int stride = bmpData.Stride;
					int bufferSize = stride * height;
					byte[] argbData = new byte[bufferSize];
					Marshal.Copy(bmpData.Scan0, argbData, 0, bufferSize);

					// WriteableBitmap 期望 BGRA 顺序
					byte[] bgraData = new byte[bufferSize];
					for (int i = 0; i < argbData.Length; i += 4)
					{
						bgraData[i] = argbData[i];     // B
						bgraData[i + 1] = argbData[i + 1]; // G
						bgraData[i + 2] = argbData[i + 2]; // R
						bgraData[i + 3] = argbData[i + 3]; // A
					}
					return new IconPixelData(width, height, bgraData);
				}
				finally
				{
					bitmap.UnlockBits(bmpData);
				}
			}
		}
	}
}


