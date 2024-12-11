/** @file PhotoDialog.xaml.cs
 *  @brief 照片檢視器

 *  這個視窗包含了一個簡單的相片顯示器，並且實作了另存檔案的功能。

 *  @author Shu-Kai Yang (skyang@csie.nctu.edu.tw)
 *  @date 2024/6/14 */

using System;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BrowseEpub
{
    public partial class PhotoDialog : Window
    {
        /// <summary>
        ///  此對話方塊要觀察的照片項目。
        /// </summary>
        public Photo Photo = null;

        /// <summary>
        ///  此照片的旋轉角度:
        /// </summary>
        private BitmapImage m_bitmap = null;
        private int m_rotateDrgrees = 0;

        /// 來自設定檔的視窗尺寸與位置紀錄:
        static public System.Drawing.Size ViewerSize = new System.Drawing.Size(1280, 600);
        static public System.Drawing.Point ViewerPos = new System.Drawing.Point(0, 0);

        public PhotoDialog()
        {
            InitializeComponent();

            /// 載入先前儲存的視窗尺寸與位置。
            this.Left = ViewerPos.X;
            this.Top = ViewerPos.Y;
            this.Width = ViewerSize.Width;
            this.Height = ViewerSize.Height;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {   PrevNextButton_Click(null, null);  }

        /// <summary>
        ///  提供一些常見的快捷鍵。
        /// </summary>
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Left) || (e.Key == Key.PageUp))
            {
                PrevNextButton_Click(PrevButton, null);
                e.Handled = true;
                return;
            }

            if ((e.Key == Key.Right) || (e.Key == Key.PageDown))
            {
                PrevNextButton_Click(NextButton, null);
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.R)
                {
                    RotateButton_Click(null, null);
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.S)
                {
                    SaveAsButton_Click(null, null);
                    e.Handled = true;
                    return;
                }
            }
            else if (e.Key == Key.Escape)
            {
                OkButton_Click(null, null);
                e.Handled = true;
            }
        }

        /// <summary>
        ///  上一張與下一張照片的按鈕。
        /// </summary>
        private void PrevNextButton_Click(object sender, RoutedEventArgs e)
        {
            /// 移動目前的照片選擇:
            int photoCount = Photo.Paragraph.Photos.Count;
            int index = Photo.Index;

            if (sender != null)
            {
                if (sender.Equals(PrevButton) == true)
                {
                    --index;
                    if (index < 0) {  index = 0;  }
                }
                else if (sender.Equals(NextButton) == true)
                {
                    ++index;
                    if (index == photoCount) {  index = photoCount - 1;  }
                }

                Photo = Photo.Paragraph.Photos[index];
            }

            /// 在視窗標題和頁面控制項顯示 Photo 的資訊。
            m_bitmap = Photo.PhotoImageSrc;

            int i = index + 1;
            Title = Photo.FileName + " ( " + i + " of " + photoCount + " )";

            m_rotateDrgrees = Photo.RotateDegrees;
            if (m_rotateDrgrees != 0)
            {
                RotateTransform transform = new RotateTransform(m_rotateDrgrees);
                TransformedBitmap bmp = new TransformedBitmap();
                bmp.BeginInit();
                bmp.Source = m_bitmap;
                bmp.Transform = transform;
                bmp.EndInit();
                PhotoViewer.Source = bmp;
            }
            else
            {   PhotoViewer.Source = m_bitmap;  }

            MessageLabel.Text = Photo.Description;
        }

        /// <summary>
        ///  旋轉目前的照片。
        /// </summary>
        private void RotateButton_Click(object sender, RoutedEventArgs e)
        {
            m_rotateDrgrees += 90;
            if (m_rotateDrgrees == 360) {  m_rotateDrgrees = 0;  }
            if (m_bitmap == null) {  return;  }

            if (m_rotateDrgrees != 0)
            {
                RotateTransform transform = new RotateTransform(m_rotateDrgrees);

                TransformedBitmap bmp = new TransformedBitmap();
                bmp.BeginInit();
                bmp.Source = m_bitmap;
                bmp.Transform = transform;
                bmp.EndInit();
                PhotoViewer.Source = bmp;
            }
            else
            {   PhotoViewer.Source = m_bitmap;  }
        }

        /// <summary>
        ///  從 EPUB 中解壓縮目前的圖檔。
        /// </summary>
        private void SaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;
            String fileExt = Path.GetExtension(Photo.FileName).ToLower();

            /// 開啟檔案對話方塊以選擇輸出的檔案名稱與類型:
            System.Windows.Forms.SaveFileDialog dialog = new System.Windows.Forms.SaveFileDialog();
            if (fileExt.Equals(".png")) { dialog.Filter = "PNG files (*.png)|*.png"; }
            else {  dialog.Filter = "JPEG files (*.jpg;*.jpeg)|*.jpg;*.jpeg";  }
            dialog.Title = Properties.Resources.ExportPhotoMsg;
            dialog.FileName = Photo.FileName;
            dialog.DefaultExt = fileExt;
            dialog.OverwritePrompt = true;

            System.Windows.Forms.DialogResult result = dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                /// 解壓縮檔案:
                using (FileStream fileStream = File.OpenRead(Photo.ZipPathName))
                {
                    using (ZipArchive archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                    {
                        ZipArchiveEntry entry = archive.GetEntry(Photo.ZipEntry);
                        if (entry != null)
                        {
                            try {  ZipFileExtensions.ExtractToFile(entry, dialog.FileName);  }
                            catch (Exception ex)
                            {
                                MessageBox.Show(ex.Message, Properties.Resources.Error);
                                return;
                            }

                            MessageBox.Show(Properties.Resources.FileExportedOK + dialog.FileName);
                        }
                    }
                }               
            }
        }

        /// <summary>
        ///  儲存視窗尺寸與位置並且關閉視窗。
        /// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                ViewerPos = new System.Drawing.Point((int)this.Left, (int)this.Top);
                ViewerSize = new System.Drawing.Size((int)this.Width, (int)this.Height);
            }
            else
            {
                ViewerPos = new System.Drawing.Point((int)RestoreBounds.Left, (int)RestoreBounds.Top);
                ViewerSize = new System.Drawing.Size((int)RestoreBounds.Size.Width, (int)RestoreBounds.Size.Height);
            }

            DialogResult = true;
        }
    }
}
