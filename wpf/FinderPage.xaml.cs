/** @file FinderPage.xaml.cs
 *  @brief 在相簿中尋找

 *  這個頁面使用雙執行緒來進行相簿中的搜尋，背景執行緒 m_worker 會把符合條件的相簿檔案中的 XML 解壓
 *  縮，並且檢查相簿內各篇章的段落拍攝日期、地點、內文裡是否包含使用者指定的字詞，若是則將它們放到
 *  m_paraQueue 裡，由 UI thread 在 OnSearchProgressChanged() 當中逐批地為這些段落產生對應的頁面控
 *  制項，找到的段落包含其相簿和篇章標題，會產生 TextBlock, Button, 以及 Image 控制項，並且附加 Tag
 *  以及前往 ReaderPage 或 PhotoDialog 的事件處理常式。

 *  為了效率起見，XML 檔案和段落照片縮圖檔案的解壓縮會全部在 m_worker 裡進行，在過程中會以 progress
 *  bar 顯示進度，並可由使用者按下取消按鈕來中斷搜尋並保留目前的搜尋結果。

 *  @author Shu-Kai Yang (skyang@csie.nctu.edu.tw)
 *  @date 2024/5/9 */

using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;

namespace BrowseEpub
{
    public partial class FinderPage : Page
    {
        /// <summary>
        ///  在背景執行的搜尋執行緒。
        /// </summary>
        private BackgroundWorker m_worker = null;

        /// 因為在 m_worker 中不能存取 UI 物件，因此事先將控制項中的數值備份起來:
        private DateTime m_beginDate = DateTime.Now.AddYears(-1);
        private DateTime m_endDate = DateTime.Now;
        private String m_titleSearch = String.Empty;
        private String m_authorSearch = String.Empty;
        private String m_locationSearch = String.Empty;
        private String m_textSearch = String.Empty;


        /// 以 List 實作 Queue 行為，在 m_worker 當中將新找到的段落 m_paraQueue.Add()，並在 report
        /// progress 的時候告知數量，UI thread 在 progress changed 事件中由 m_queueIndex 取出該數量
        /// 的 Paragraph 建立控制項並備份到 wnd.FoundParas 裡。
        private List<Paragraph> m_paraQueue = null;
        private int m_queueIndex = 0;

        /// 在由 Paragraph 建立畫面的控制項的時候，從這裡判斷要不要建立 Album 或 TocItem 的項目:
        private Album m_curAlbum = null;
        private TocItem m_curChap = null;

        public FinderPage()
        {   InitializeComponent();  }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;
            wnd.Title = Properties.Resources.FindInAlbums + " - " + Properties.Resources.AppTitle;

            /// 從 wnd 取回先前的搜尋參數:
            BeginDatePicker.SelectedDate = wnd.BeginDate;
            EndDatePicker.SelectedDate = wnd.EndDate;
            AlbumTitleTextBox.Text = wnd.TitleSearch;
            AlbumAuthorTextBox.Text = wnd.AuthorSearch;
            LocationTextBox.Text = wnd.LocationSearch;
            ParagraphTextBox.Text = wnd.TextSearch;

            /// 復原先前的搜尋結果:
            if (wnd.FoundParas != null)
            {
                foreach (Paragraph para in wnd.FoundParas)
                {   AddParagraphCtrl(para);  }
            }
        }

        /// <summary>
        ///  清除目前的搜尋結果。
        /// </summary>
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            /// 等待搜尋執行緒結束:
            while (m_worker != null) {  Thread.Sleep(50);  }

            /// 重置畫面控制項的狀態:
            AlbumTitleTextBox.Text = String.Empty;
            AlbumAuthorTextBox.Text = String.Empty;
            LocationTextBox.Text = String.Empty;
            ParagraphTextBox.Text = String.Empty;

            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;
            wnd.TitleSearch = String.Empty;
            wnd.AuthorSearch = String.Empty;
            wnd.LocationSearch = String.Empty;
            wnd.TextSearch = String.Empty;

            ParagraphPanel.Children.Clear();
            wnd.FoundParas = null;

            MessageLabel.Text = Properties.Resources.SearchHintMsg;
        }

        /// <summary>
        ///  返回上一頁。
        /// </summary>
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            /// 清除資料:
            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;
            wnd.TitleSearch = String.Empty;
            wnd.AuthorSearch = String.Empty;
            wnd.LocationSearch = String.Empty;
            wnd.TextSearch = String.Empty;
            wnd.FoundParas = null;

            /// 返回上一頁:
            if (this.NavigationService.CanGoBack)
            {   this.NavigationService.GoBack();  }
        }

        /// ///////////////////////////////////////////////////////////////////////////////////////
        /// 以多執行緒進行可取消的搜尋作業，原理是準備一個空的 m_paraQueue 並重設相關變數，初始化背
        /// 景執行緒 m_worker 執行 DoSearchWork() 函式，並把進度回報給 OnSearchProgressChanged() 以
        /// 及 OnSearchCompleted()，由 DoSearchWork() 負責解壓縮相簿內的 XML 進行內文比對，並且把符
        /// 合的段落放進 m_paraQueue 中，另外兩個函式將段落取出，並且呼叫 AddParagraphCtrl() 來為它
        /// 們建立頁面上的控制項，如果在過程中取消了，已經建立的控制項仍然會留在頁面上。
        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (m_worker != null) {  return;  }

            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;
            wnd.FoundParas = null;
            ParagraphPanel.Children.Clear();

            /// 準備工作:
            m_beginDate = (DateTime)BeginDatePicker.SelectedDate;
            m_endDate = (DateTime)EndDatePicker.SelectedDate;
            if (DateTime.Compare(m_endDate, m_beginDate) < 0) {  m_endDate = m_beginDate;  }

            m_titleSearch = AlbumTitleTextBox.Text.Trim();
            m_authorSearch = AlbumAuthorTextBox.Text.Trim();
            m_locationSearch = LocationTextBox.Text.Trim();
            m_textSearch = ParagraphTextBox.Text.Trim();

            m_paraQueue = new List<Paragraph>();
            m_queueIndex = 0;
            m_curAlbum = null;
            m_curChap = null;
            SearchProgBar.Value = 0;

            /// 建立 BackgroundWorker 執行搜尋工作:
            m_worker = new BackgroundWorker();
            m_worker.WorkerReportsProgress = true;
            m_worker.WorkerSupportsCancellation = true;
            m_worker.DoWork += new DoWorkEventHandler(DoSearchWork);
            m_worker.ProgressChanged += new ProgressChangedEventHandler(OnSearchProgressChanged);
            m_worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(OnSearchCompleted);

            MessageLabel.Text = Properties.Resources.StartSearching;
            m_worker.RunWorkerAsync();
        }

        /// <summary>
        ///  在任何欄位按下 Enter 都會啟動搜尋。
        /// </summary>
        private void TokenTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {   SearchButton_Click(null, null);  }
        }

        /// <summary>
        ///  這個函式會在 m_worker 當中執行。
        /// </summary>
        private void DoSearchWork(object sender, DoWorkEventArgs e)
        {
            App app = Application.Current as App;
            app.ThreadIsRunning = true;

            /// 先把符合日期範圍的相簿整理為 albums 集合:
            List<AlbumInfo> candidates = new List<AlbumInfo>();

            foreach (AlbumInfo info in app.Albums)
            {
                bool albumIsValid = true;
                if ((DateTime.Compare(info.FirstDate, m_endDate) > 0) ||
                   (DateTime.Compare(info.LastDate, m_beginDate) < 0))
                {   albumIsValid = false;  }

                if (m_titleSearch.Equals(String.Empty) == false)
                {
                    if (info.Title.IndexOf(m_titleSearch, StringComparison.OrdinalIgnoreCase) == -1)
                    {   albumIsValid = false;  }
                }

                if (m_authorSearch.Equals(String.Empty) == false)
                {
                    if (info.Author.IndexOf(m_authorSearch, StringComparison.OrdinalIgnoreCase) == -1)
                    {   albumIsValid = false;  }
                }

                if (albumIsValid == true)
                {   candidates.Add(info);  }
            }

            if ((app.CancelTheThread == true) || (m_worker.CancellationPending == true))
            {   e.Cancel = true;  return;  }

            for (int i=0; i<candidates.Count; ++i)
            {
                /// 對於符合日期範圍和標題作者條件的相簿 candidate，試圖解壓縮當中的 XML 以建立 Body:
                AlbumInfo candidate = candidates[i];
                if (candidate.Body == null)
                {
                    String pathName = Path.Combine(app.WorkDir, candidate.FileName);
                    if (File.Exists(pathName) == true)
                    {
                        Album album = new Album();
                        album.PathName = pathName;
                        if (album.LoadXml() == true) {  candidate.Body = album;  }
                    }
                }

                if ((app.CancelTheThread == true) || (m_worker.CancellationPending == true))
                {   e.Cancel = true;  return;  }

                /// 對於 candidate 中的每個章節每個段落，進一步檢查照片的日期、地點、段落內文是否符合條件:
                if (candidate.Body != null)
                {
                    int found = 0;
                    foreach (TocItem item in candidate.Body.Toc)
                    {
                        if (item.IsChapter == true)
                        {
                            /// 如果標題符合文字搜尋條件，那麼整個篇章都符合條件:
                            bool chapterIsValid = false;
                            if (String.IsNullOrEmpty(m_textSearch) == false)
                            {
                                if (item.Title.IndexOf(m_textSearch, StringComparison.OrdinalIgnoreCase) != -1)
                                {   chapterIsValid = true;  }
                            }

                            if (chapterIsValid == true)
                            {
                                foreach (Paragraph para in item.Paragraphs)
                                {
                                    m_paraQueue.Add(para);
                                    ++found;

                                    /// 把縮圖的解壓縮工作也在 m_worker 裡完成:
                                    foreach (Photo photo in para.Photos)
                                    {  photo.PreloadThumbBytes();  }
                                }
                            }
                            else
                            {
                                /// 檢查每個段落是否符合條件:
                                foreach (Paragraph para in item.Paragraphs)
                                {
                                    bool paraIsValid = true;
                                    if (para.DateIsVisible == true)
                                    {
                                        if ((DateTime.Compare(para.Date, m_endDate) > 0) ||
                                            (DateTime.Compare(para.Date, m_beginDate) < 0))
                                        {   paraIsValid = false;  }
                                    }

                                    if (m_locationSearch.Equals(String.Empty) == false)
                                    {
                                        if (para.LocationIsVisible == true)
                                        {
                                            if (para.Location.IndexOf(m_locationSearch, StringComparison.OrdinalIgnoreCase) == -1)
                                            {   paraIsValid = false;  }
                                        }
                                        else {   paraIsValid = false;  }
                                    }

                                    if (String.IsNullOrEmpty(m_textSearch) == false)
                                    {
                                        if (String.IsNullOrEmpty(para.Context) == false)
                                        {
                                            if (para.Context.IndexOf(m_textSearch, StringComparison.OrdinalIgnoreCase) == -1)
                                            {   paraIsValid = false;  }
                                        }
                                        else  {  paraIsValid = false;  }

                                        /// 如果段落有標題，也檢查它是否包含 m_textSearch 字詞:
                                        if ((paraIsValid == false) && (String.IsNullOrEmpty(para.Title) == false))
                                        {
                                            if (para.Title.IndexOf(m_textSearch, StringComparison.OrdinalIgnoreCase) != -1)
                                            {   paraIsValid = true;  }
                                        }

                                        /* 檢查段落內每一張照片的註解是否包含 m_textSearch 字詞:
                                        if (paraIsValid == false)
                                        {
                                            foreach (Photo photo in para.Photos)
                                            {
                                                if (String.IsNullOrEmpty(photo.Description) == false)
                                                {
                                                    if (photo.Description.IndexOf(m_textSearch, StringComparison.OrdinalIgnoreCase) != -1)
                                                    {   paraIsValid = true;  break;  }
                                                }
                                            }
                                        } */
                                    }

                                    if (paraIsValid == true)
                                    {
                                        m_paraQueue.Add(para);
                                        ++found;

                                        /// 把縮圖的解壓縮工作也在 m_worker 裡完成:
                                        foreach (Photo photo in para.Photos)
                                        {   photo.PreloadThumbBytes();  }
                                    }

                                    if ((app.CancelTheThread == true) || (m_worker.CancellationPending == true))
                                    {   e.Cancel = true;  return;  }
                                }
                            }
                        }
                    }

                    m_worker.ReportProgress((int)(((i + 1) * 100) / candidates.Count), found);
                }
            }

            app.ThreadIsRunning = false;
        }

        /// <summary>
        ///  當 m_worker 分析完一本相簿的時候會讓 UI thread 呼叫這個函式。
        /// </summary>
        private void OnSearchProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            int count = 0;
            SearchProgBar.Value = e.ProgressPercentage;
            if (e.UserState != null) {  count = (int)e.UserState;  }

            /// 這次報告進度共找到 count 個 Paragraph，將它們加入到畫面上:
            for (int i = 0; i < count; ++i)
            {   AddParagraphCtrl(m_paraQueue[m_queueIndex]);  ++m_queueIndex;  }

            /// 顯示目前的數量:
            MessageLabel.Text = Properties.Resources.FoundParagraphs + " " + m_paraQueue.Count.ToString();
        }

        /// <summary>
        ///  當 m_worker 處理完所有工作的時候會讓 UI thread 呼叫這個函式。
        /// </summary>
        private void OnSearchCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;
            app.CancelTheThread = false;

            /// 處理 m_paraQueue 中剩餘的項目:
            if (m_paraQueue != null)
            {
                while (m_queueIndex < m_paraQueue.Count)
                {
                    AddParagraphCtrl(m_paraQueue[m_queueIndex]);
                    ++m_queueIndex;
                }
            }

            /// 顯示取消或完成的訊息:
            if (e.Cancelled)
            {   MessageLabel.Text = Properties.Resources.UserCancelMsg;  }
            else if (e.Error != null)
            {   MessageLabel.Text = e.Error.Message;  }
            else
            {
                if ((m_paraQueue == null) || (m_paraQueue.Count == 0))
                {   MessageLabel.Text = Properties.Resources.NoResultMsg;  }
                else
                {
                    MessageLabel.Text = Properties.Resources.FoundParagraphs + " " + m_paraQueue.Count.ToString();
                 /* MessageLabel.Text = Properties.Resources.Done; */
                }

                SearchProgBar.Value = 100;
            }

            /// 備份搜尋參數:
            wnd.BeginDate = m_beginDate;
            wnd.EndDate = m_endDate;
            wnd.TitleSearch = m_titleSearch;
            wnd.AuthorSearch = m_authorSearch;
            wnd.LocationSearch = m_locationSearch;
            wnd.TextSearch = m_textSearch;

            /// 把整個 queue 備份到 wnd.FoundParas 中:
            wnd.FoundParas = m_paraQueue;
            m_paraQueue = null;
            m_worker = null;
        }

        /// <summary>
        ///  要求搜尋的子執行緒停止。
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;
            if (app.ThreadIsRunning) {  app.CancelTheThread = true;  }
            if (m_worker != null) {   m_worker.CancelAsync();  }
        }

        /// ///////////////////////////////////////////////////////////////////////////////////////
        /// 搜尋結果的控制項處理，由控制項的 Tag 取出目標物，開啟 ReaderPage 或 PhotoDialog 檢視之。
        private void AlbumLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (m_worker != null) {  return;  }

            Button button = sender as Button;
            Album album = button.Tag as Album;
            album.CurTocItem = null;

            ReaderPage page = new ReaderPage();
            page.Album = album;
            NavigationService.Navigate(page);
        }

        private void ChapterLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (m_worker != null) {  return;  }

            Button button = sender as Button;
            TocItem chapter = button.Tag as TocItem;
            Album album = chapter.Album as Album;
            album.CurTocItem = chapter;

            ReaderPage page = new ReaderPage();
            page.Album = album;
            NavigationService.Navigate(page);
        }

        private void Photo_MouseDown(object sender, MouseEventArgs e)
        {
            if (m_worker != null) {  e.Handled = true;  return;  }

            Image image = sender as Image;
            Photo photo = image.Tag as Photo;
            e.Handled = true;

            PhotoDialog dialog = new PhotoDialog();
            dialog.Photo = photo;
            dialog.ShowDialog();
        }

        /// <summary>
        ///  在 ParagraphPanel 裡面加入關聯 para 的控制項，並指定事件處理常式。
        /// </summary>
        private void AddParagraphCtrl(Paragraph para)
        {
            /// 是否進入新的相簿？新增此相簿的連結
            if ((m_curAlbum == null) || (para.Chapter.Album != m_curAlbum))
            {
                if (m_curAlbum != null) {  ParagraphPanel.Children.Add(new Separator());  }
                m_curAlbum = para.Chapter.Album;

                StackPanel panel = new StackPanel();
                panel.Orientation = Orientation.Horizontal;
                panel.Margin = new Thickness(0, 4, 0, 4);

                TextBlock textBlock = new TextBlock();
                textBlock.Style = FindResource("AlbumTitleTextStyle") as Style;
                textBlock.Text = Properties.Resources.Album + ": " + m_curAlbum.Title;
                panel.Children.Add(textBlock);

                Button button = new Button();
                button.Style = FindResource("LinkButtonStyle") as Style;
                button.Margin = new Thickness(16, 0, 0, 0);
                button.Content = Properties.Resources.More;
                button.Tag = m_curAlbum;
                button.Click += new RoutedEventHandler(AlbumLinkButton_Click);
                panel.Children.Add(button);
                ParagraphPanel.Children.Add(panel);

                if (m_worker != null)
                {  MessageLabel.Text = Properties.Resources.SearchingInAlbum + m_curAlbum.Title;  }
            }

            /// 是否進入新的章節？新增此章節的連結:
            if ((m_curChap == null) || (para.Chapter != m_curChap))
            {
                m_curChap = para.Chapter;

                StackPanel panel = new StackPanel();
                panel.Orientation = Orientation.Horizontal;
                panel.Margin = new Thickness(0, 4, 0, 4);

                TextBlock textBlock = new TextBlock();
                textBlock.Style = FindResource("ChapterTitleTextStyle") as Style;
                textBlock.Text = Properties.Resources.Chapter + ": " + m_curChap.Title;
                panel.Children.Add(textBlock);

                Button button = new Button();
                button.Style = FindResource("LinkButtonStyle") as Style;
                button.Margin = new Thickness(16, 0, 0, 0);
                button.Content = Properties.Resources.More;
                button.Tag = m_curChap;
                button.Click += new RoutedEventHandler(ChapterLinkButton_Click);
                panel.Children.Add(button);
                ParagraphPanel.Children.Add(panel);
            }

            /// 新增此段落的控制項:
            if ((para.DateIsVisible == true) || (para.LocationIsVisible == true))
            {
                TextBlock textBlock = new TextBlock();
                textBlock.Style = FindResource("ParagraphTextStyle") as Style;
                textBlock.FontWeight = FontWeights.Bold;

                if (para.LocationIsVisible == false)
                {   textBlock.Text = para.Date.ToString(AlbumInfo.DateFormat);  }
                else if (para.DateIsVisible == false)
                {   textBlock.Text = para.Location;  }
                else
                {
                    textBlock.Text = para.Date.ToString(AlbumInfo.DateFormat)
                     + " " + Properties.Resources.At + " " + para.Location;
                }

                ParagraphPanel.Children.Add(textBlock);
            }

            if (String.IsNullOrEmpty(para.Context) == false)
            {
                TextBlock textBlock = new TextBlock();
                textBlock.Style = FindResource("ParagraphTextStyle") as Style;
                textBlock.Text = para.Context;
                ParagraphPanel.Children.Add(textBlock);
            }

            if (para.Photos.Count > 0)
            {
                WrapPanel panel = new WrapPanel();
                panel.Orientation = Orientation.Horizontal;
                panel.Margin = new Thickness(0, 8, 0, 32);

                foreach (Photo photo in para.Photos)
                {
                    Image image = new Image();
                    image.Margin = new Thickness(8);
                    image.Width = 240;
                    image.Height = 200;
                    image.Stretch = Stretch.Uniform;
                    image.Tag = photo;
                    image.Source = photo.ThumbImageSrc;
                    image.MouseLeftButtonDown += new MouseButtonEventHandler(Photo_MouseDown);
                    panel.Children.Add(image);
                }

                ParagraphPanel.Children.Add(panel);
            }

            /// 已增加了新的控制項故刷新頁面:
            ParagraphPanel.UpdateLayout();
        }
    }
}
