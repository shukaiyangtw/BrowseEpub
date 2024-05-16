/** @file ReaderPage.xaml.cs
 *  @brief 閱讀相簿內容

 *  這個頁面的左邊 ListView 是相簿的篇章目錄(TOC)，右邊 WebBrowser 顯示篇章內容。

 *  @author Shu-Kai Yang (skyang@csie.nctu.edu.tw)
 *  @date 2024/5/11 */

using System;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Threading.Tasks;
using System.Diagnostics;

namespace BrowseEpub
{
    public partial class ReaderPage : Page
    {
        /// <summary>
        ///  本頁面正閱讀中的相簿結構，可能是在 FinderPage 已經載入好的 Album 物件。
        /// </summary>
        public Album Album = null;
        private String m_cacheFolder = String.Empty;

        /// <summary>
        ///  解壓縮過程中發生的錯誤訊息。
        /// </summary>
        public String LastError = String.Empty;

        public ReaderPage()
        {   InitializeComponent();  }

        /// <summary>
        ///  在此解壓縮開啟電子書所需的最底限度檔案。
        /// </summary>
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;
            wnd.Title = Album.Title + " - " + Properties.Resources.AppTitle;

            /// 準備此相簿的快取目錄內容:
            m_cacheFolder = Path.Combine(app.DataDir, Album.Identifier);
 
            if ((Directory.Exists(m_cacheFolder) == true) && (m_cacheFolder.StartsWith(app.WorkDir) == false))
            {
                /// 如果相簿不是工作目錄內的檔案，每次都刪除舊快取內容(因為無法確認版本是否夠新):
                try {  Directory.Delete(m_cacheFolder, true);  } catch { }
            }

            MessageLabel.Text = Properties.Resources.InflateFilesMsg;
            ImportProgBar.IsIndeterminate = true;

            /// 準備相關的檔案:
            await Task.Run(() => PrepareCacheDir());

            /// 已經載入相簿的資料結構，視需要解壓縮必要的檔案:
            if (String.IsNullOrEmpty(LastError) == true)
            {
                Directory.SetCurrentDirectory(m_cacheFolder);
                if (Album.CurTocItem == null) {  Album.CurTocItem = Album.Toc[0];  }
                String filePath = Path.Combine(m_cacheFolder, Album.CurTocItem.HtmlFileName);
                if (File.Exists(filePath) == false) {  await Task.Run(() => PrepareCurTocItem());  }
            }
            else {  MessageLabel.Text = LastError;  }
 
            ImportProgBar.IsIndeterminate = false;

            if (String.IsNullOrEmpty(LastError) == true)
            {
                /// 沒有任何錯誤訊息，顯示就緒訊息:
                MessageLabel.Text = Properties.Resources.Ready;

                /// XhtmlView 顯示 CurTocItem 指定的網頁:
                TocView.ItemsSource = Album.Toc;
                String filePath = Path.Combine(m_cacheFolder, Album.CurTocItem.HtmlFileName);
                String fileUrl = "file:///" + filePath.Replace('\\', '/');
                XhtmlView.Navigate(fileUrl);
            }
        }

        /// <summary>
        ///  從 Album 解壓縮必要的檔案到 wnd.CacheDir 中。
        /// </summary>
        private Task PrepareCacheDir()
        {
            App app = Application.Current as App;
            app.ThreadIsRunning = true;

            if (Directory.Exists(m_cacheFolder) == false)
            { 
                try {  Directory.CreateDirectory(m_cacheFolder);  }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    Debug.WriteLine(LastError);
                    app.ThreadIsRunning = false;
                    return Task.CompletedTask;
                }
            }

            String pathName = Path.Combine(m_cacheFolder, "style.css");
            if (File.Exists(pathName) == false)
            {
                using (FileStream fileStream = File.OpenRead(Album.PathName))
                {
                    using (ZipArchive archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                    {
                        ZipArchiveEntry entry = archive.GetEntry("EPUB/style.css");
                        if (entry != null)
                        {
                        
                            try {  ZipFileExtensions.ExtractToFile(entry, pathName);  }
                            catch (Exception ex)
                            {
                                LastError = ex.Message;
                                Debug.WriteLine(LastError);
                            }
                        }

                        entry = archive.GetEntry("EPUB/title.xhtml");
                        if (entry != null)
                        {
                            pathName = Path.Combine(m_cacheFolder, "title.xhtml");
                            try {  ZipFileExtensions.ExtractToFile(entry, pathName);  }
                            catch (Exception ex)
                            {
                                LastError = ex.Message;
                                Debug.WriteLine(LastError);
                            }
                        }
                    }
                }
            }

            app.CancelTheThread = false;
            app.ThreadIsRunning = false;
            return Task.CompletedTask;
        }

        /// <summary>
        ///  從 CurTocItem 解壓縮必要的檔案到 wnd.CacheDir 中。
        /// </summary>
        private Task PrepareCurTocItem()
        {
            App app = Application.Current as App;
            app.ThreadIsRunning = true;

            using (FileStream fileStream = File.OpenRead(Album.PathName))
            {
                using (ZipArchive archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                {
                    /// 解壓此篇章的網頁檔案:
                    ZipArchiveEntry entry = archive.GetEntry("EPUB/" + Album.CurTocItem.HtmlFileName);
                    if (entry != null)
                    {
                        String destPathName = Path.Combine(m_cacheFolder, Album.CurTocItem.HtmlFileName);
                        try {  ZipFileExtensions.ExtractToFile(entry, destPathName);  }
                        catch (Exception ex)
                        {
                            LastError = ex.Message;
                            Debug.WriteLine(LastError);
                        }
                    }

                    if (Album.CurTocItem.IsChapter == true)
                    {
                        /// 解壓縮此篇章裡每張照片的縮圖:
                        String chapDir = Path.Combine(m_cacheFolder, Album.CurTocItem.Directory);
                        Directory.CreateDirectory(chapDir);
                        String thumbDir = Path.Combine(chapDir, "thumbs");
                        Directory.CreateDirectory(thumbDir);

                        foreach (Paragraph para in Album.CurTocItem.Paragraphs)
                        {
                            foreach (Photo photo in para.Photos)
                            {
                                entry = archive.GetEntry(photo.ZipEntryThumb);
                                if (entry != null)
                                {
                                    String destPathName = Path.Combine(thumbDir, photo.FileName);
                                    try { ZipFileExtensions.ExtractToFile(entry, destPathName); }
                                    catch (Exception ex)
                                    {
                                        LastError = ex.Message;
                                        Debug.WriteLine(LastError);
                                    }
                                }
                            }
                        } 
                    }
                }
            }

            app.CancelTheThread = false;
            app.ThreadIsRunning = false;
            return Task.CompletedTask;
        }

        /// <summary>
        ///  點擊了 TocView 中的章節項目。
        /// </summary>
        private async void TocView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TocView.SelectedItem != null)
            {
                TocItem tocItem = TocView.SelectedItem as TocItem;
                Album.CurTocItem = tocItem;

                String pathName = Path.Combine(m_cacheFolder, tocItem.HtmlFileName);
                if (File.Exists(pathName) == false)
                { 
                    MessageLabel.Text = Properties.Resources.InflateFilesMsg;
                    LastError = String.Empty;

                    ImportProgBar.IsIndeterminate = true;
                    await Task.Run(() => PrepareCurTocItem());
                    ImportProgBar.IsIndeterminate = false;

                    if (String.IsNullOrEmpty(LastError))
                    {   MessageLabel.Text = Properties.Resources.Ready;  }
                    else {  MessageLabel.Text = LastError;  }
                }
                
                String fileUrl = "file:///" + pathName.Replace('\\', '/');
                XhtmlView.Navigate(fileUrl);
            }
        }

        /// <summary>
        ///  檢查要前往的頁面是章節還是照片。
        /// </summary>
        private void XhtmlView_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            /// 不允許單純地刷新畫面:
            if (e.NavigationMode == NavigationMode.Refresh)
            {   e.Cancel = true;  return;  }

            /// 如果是外部網址，另外開啟瀏覽器:
            String url = e.Uri.OriginalString;
            if ((url.StartsWith("https:") == true) || (url.StartsWith("http:") == true))
            {
                e.Cancel = true;
                Process.Start(url);
                return;
            }

            /// 如果是連往一張圖片，直接用 PhotoDialog 來開啟它:
            String fileName = Path.GetFileName(url);
            if (fileName.EndsWith(".wj6.xhtml") == true)
            {
                e.Cancel= true;
                String fileTitle = fileName.Substring(0, fileName.Length - 10);
                Photo photo = Album.CurTocItem.FindPhoto(fileTitle);
                if (photo != null)
                {
                    PhotoDialog dialog = new PhotoDialog();
                    dialog.Photo = photo;
                    dialog.ShowDialog();
                }
            }
        }

        /// <summary>
        ///  返回上一頁。
        /// </summary>
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.NavigationService.CanGoBack)
            {   this.NavigationService.GoBack();  }
        }
    }
}
