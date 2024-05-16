/** @file NavigationWindow.xaml.cs
 *  @brief 主要導覽視窗

 *  這個衍生自 NavigationWindow 的視窗用來導覽 AlbumListPage, FinderPage 及 ReaderPage，並且覆寫了
    NavigationWindow_Closing() 結束時呼叫 SaveAlbumsXml() 將相簿資訊儲存回 albums.xml。

 *  @author Shu-Kai Yang (skyang@csie.nctu.edu.tw)
 *  @date 2024/5/8 */

using System;
using System.IO;
using System.Data;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Navigation;
using System.Threading;
using System.Diagnostics;
using BrowseEpub.Properties;

namespace BrowseEpub
{
    public partial class MainWindow : NavigationWindow
    {
        /// <summary>
        ///  可選擇閱覽模式與排序模式。
        /// </summary>
        public const int DETAILVIEW = 0;
        public const int ICONVIEW = 1;

        public const int ORDER_BY_DATE = 0;
        public const int ORDER_BY_TITLE = 1;
        public const int ORDER_BY_AUTHOR = 3;
        public const int ORDER_BY_FILENAME = 4;

        /// 閱覽模式與排序選項:
        public Int32 ViewMode = 0;
        public Int32 SortOption = 0;
        public Boolean IsAscending = true;

        /// <summary>
        ///  為 AlbumListPage 暫存目前顯示的資料。
        /// </summary>
        public List<AlbumInfo> DataView = null;
        public AlbumInfo SelectedAlbum = null;
        public String FileSearch = String.Empty;
        public Int32 ListScrollPos = 0;

        /// <summary>
        ///  為 FinderListPage 暫存目前的搜尋參數和結果。
        /// </summary>
        public List<Paragraph> FoundParas = null;
        public DateTime BeginDate = DateTime.Now.AddYears(-1);
        public DateTime EndDate = DateTime.Now;
        public String TitleSearch = String.Empty;
        public String AuthorSearch = String.Empty;
        public String LocationSearch = String.Empty;
        public String TextSearch = String.Empty;

        #region Read/Write of Settings.Default.
        public MainWindow()
        {
            InitializeComponent();
            Title = Properties.Resources.AppTitle;
            this.Navigating += OnNavigating;

            /// 載入先前儲存的視窗尺寸與位置。
            if (Settings.Default.WindowPos != null)
            {
                this.Left = Settings.Default.WindowPos.X;
                this.Top = Settings.Default.WindowPos.Y;
            }

            if (Settings.Default.WindowSize != null)
            {
                this.Width = Settings.Default.WindowSize.Width;
                this.Height = Settings.Default.WindowSize.Height;
            }

            if (Settings.Default.ViewerPos != null)
            {
                PhotoDialog.ViewerPos.X = Settings.Default.ViewerPos.X;
                PhotoDialog.ViewerPos.Y = Settings.Default.ViewerPos.Y;
            }

            if (Settings.Default.ViewerSize != null)
            {
                PhotoDialog.ViewerSize.Width = Settings.Default.ViewerSize.Width;
                PhotoDialog.ViewerSize.Height = Settings.Default.ViewerSize.Height;
            }

            /// 閱覽模式與排序選項:
            ViewMode = Settings.Default.ViewMode;
            SortOption = Settings.Default.SortOption;
            IsAscending = Settings.Default.IsAscending;

            /// 取得工作目錄(或使用預設值):
            App app = Application.Current as App;
            app.WorkDir = Settings.Default.WorkDir;
            if (String.IsNullOrEmpty(app.WorkDir))
            {
                String myDocPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                app.WorkDir = String.Format(@"{0}\Album Epubs", myDocPath);
            }
        }

        /// <summary>
        ///  讓 F5 失去作用，否則 NavigationWindow 會把 Page 銷毀再重建。
        /// </summary>
        void OnNavigating(object sender, NavigatingCancelEventArgs e)
        {
            if (e.NavigationMode == NavigationMode.Refresh)
            {   e.Cancel = true;  }
        }

        /// <summary>
        ///  在視窗要關閉之前，記錄視窗尺寸與位置。
        /// </summary>
        private void NavigationWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Debug.WriteLine("NavigationWindow is closing...");

            /// 等待執行中的子執行緒中止:
            App app = Application.Current as App;
            if (app.ThreadIsRunning)
            {
                app.CancelTheThread = true;
                while (app.ThreadIsRunning) {  Thread.Sleep(50);  }
            }

            /// 應要求清除快取:
            if (app.ClearOnExit == true)
            {
                String[] directories = Directory.GetDirectories(app.DataDir);
                foreach (String subDir in directories)
                {
                    if (subDir.Equals(app.ThumbDir) == false)
                    {   try { Directory.Delete(subDir, true); } catch { }  }
                }

                Debug.WriteLine("Album cache is cleared on exit.");
            }

            /// 紀錄視窗的尺寸與位置:
            if (this.WindowState == WindowState.Normal)
            {
                Settings.Default.WindowPos = new System.Drawing.Point((int)this.Left, (int)this.Top);
                Settings.Default.WindowSize = new System.Drawing.Size((int)this.Width, (int)this.Height);
            }
            else
            {
                Settings.Default.WindowPos = new System.Drawing.Point((int)RestoreBounds.Left, (int)RestoreBounds.Top);
                Settings.Default.WindowSize = new System.Drawing.Size((int)RestoreBounds.Size.Width, (int)RestoreBounds.Size.Height);
            }

            Settings.Default.ViewerPos = PhotoDialog.ViewerPos;
            Settings.Default.ViewerSize = PhotoDialog.ViewerSize;

            /// 紀錄閱覽模式與排序選項:
            Settings.Default.ViewMode = ViewMode;
            Settings.Default.SortOption = SortOption;
            Settings.Default.IsAscending = IsAscending;

            /// 紀錄工作目錄:
            Settings.Default.WorkDir = app.WorkDir;
            Settings.Default.Save();
            Debug.WriteLine("User preference is saved.");
        }
        #endregion

        /// <summary>
        ///  根據 SortOption 和 IsAscending 排序 DataView。
        /// </summary>
        public void SortDataView()
        {
            if ((DataView != null) && (DataView.Count > 1))
            {
                if (IsAscending == true)
                {
                    if (SortOption == MainWindow.ORDER_BY_DATE)
                    {   DataView = new List<AlbumInfo>(DataView.OrderBy(x => x.FirstDate).ThenBy(x => x.LastDate));  }
                    else if (SortOption == MainWindow.ORDER_BY_TITLE)
                    {   DataView = new List<AlbumInfo>(DataView.OrderBy(x => x.Title));  }
                    else if (SortOption == MainWindow.ORDER_BY_AUTHOR)
                    {   DataView = new List<AlbumInfo>(DataView.OrderBy(x => x.Author));  }
                    else if (SortOption == MainWindow.ORDER_BY_FILENAME)
                    {   DataView = new List<AlbumInfo>(DataView.OrderBy(x => x.FileName));  }
                }
                else
                {
                    if (SortOption == MainWindow.ORDER_BY_DATE)
                    {   DataView = new List<AlbumInfo>(DataView.OrderByDescending(x => x.FirstDate).ThenByDescending(x => x.LastDate));  }
                    else if (SortOption == MainWindow.ORDER_BY_TITLE)
                    {   DataView = new List<AlbumInfo>(DataView.OrderByDescending(x => x.Title));  }
                    else if (SortOption == MainWindow.ORDER_BY_AUTHOR)
                    {   DataView = new List<AlbumInfo>(DataView.OrderByDescending(x => x.Author));  }
                    else if (SortOption == MainWindow.ORDER_BY_FILENAME)
                    {   DataView = new List<AlbumInfo>(DataView.OrderByDescending(x => x.FileName));  }
                }
            }
        }
    }
}
