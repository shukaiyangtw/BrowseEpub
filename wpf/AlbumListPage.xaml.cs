/** @file AlbumListPage.xaml.cs
 *  @brief 管理與搜尋檔案清單

 *  這個頁面用一個 ListView 顯示工作目錄 app.WorkDir 下的所有 .album.epub 檔案，並且可以選用縮圖模式
 *  或條列模式，就像 Windows 內建的檔案總管，可以標題或檔名等進行過濾。為了達成這項功能，在 XAML 中
 *  準備了兩套 item panel 和 data template 頁面資源，並且根據檢視模式切換之。

 *  @author Shu-Kai Yang (skyang@csie.nctu.edu.tw)
 *  @date 2024/5/11 */

using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Diagnostics;

namespace BrowseEpub
{
    public partial class AlbumListPage : Page
    {
        #region Initialize/Switch the ListView display.
        /// 用於 ViewModeButton 的切換圖示影像:
        private BitmapImage DetailViewImage = null;
        private BitmapImage IconViewImage = null;

        public AlbumListPage()
        {
            InitializeComponent();
            DetailViewImage = new BitmapImage(new Uri("Assets/listview.png", UriKind.Relative));
            IconViewImage = new BitmapImage(new Uri("Assets/iconview.png", UriKind.Relative));
        }

        /// <summary>
        ///  呼叫 app.GetAlbumsInWorkDir() 取得相簿清單，並且綁定到 ListView 上。
        /// </summary>
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;

            /// 如果啟動程式時有從命令列指定要閱讀的檔名 ArgPathName，不做任何事直接前往 ReaderPage:
            if (String.IsNullOrEmpty(app.ArgPathName) == false)
            {
                Album album = new Album();
                album.PathName = app.ArgPathName;
                app.ArgPathName = String.Empty;

                MessageLabel.Text = Properties.Resources.AnalyzingEpubMsg;
                if (album.LoadXml() == false) {  MessageLabel.Text = album.LastError;  return;  }

                ReaderPage page = new ReaderPage();
                page.Album = album;
                NavigationService.Navigate(page);
                return;
            }

            /// 初始化頁面上的控制項狀態:
            MainWindow wnd = app.MainWindow as MainWindow;
            wnd.Title = Properties.Resources.AppTitle;
            WorkDirTextBox.Text = app.WorkDir;
            TokenTextBox.Text = wnd.FileSearch;

            if (wnd.SortOption == MainWindow.ORDER_BY_DATE) {  MenuItemByDate.IsChecked = true; }
            else if (wnd.SortOption == MainWindow.ORDER_BY_TITLE) {  MenuItemByTitle.IsChecked = true;  }
            else if (wnd.SortOption == MainWindow.ORDER_BY_AUTHOR) {  MenuItemByAuthor.IsChecked = true;  }
            else if (wnd.SortOption == MainWindow.ORDER_BY_FILENAME) {  MenuItemByFilename.IsChecked = true;  }

            if (wnd.IsAscending == true) {  MenuItemAscending.IsChecked = true;  }
            else{  MenuItemDescending.IsChecked = true;  }

            /// 否則從 MainWindow 取回暫存的 DataView，如果 DataView 為 null 表示尚未載入相簿清單:
            if (wnd.DataView == null)
            {
                ImportProgBar.IsIndeterminate = true;
                await Task.Run(() => app.GetAlbumsInWorkDir());
                ImportProgBar.IsIndeterminate = false;

                if (String.IsNullOrEmpty(app.LastError) == false) {  MessageLabel.Text = app.LastError;  }
                else {  MessageLabel.Text = Properties.Resources.WelcomeMsg;  }

                /// 將 app.Albums 複製到 wnd.DataView 並依選項排序之:
                if (app.Albums != null)
                {   wnd.DataView = new List<AlbumInfo> (app.Albums);  }
                else {  wnd.DataView = new List<AlbumInfo>();  }
                wnd.SortDataView();
            }
            else
            {   MessageLabel.Text = Properties.Resources.WelcomeMsg;  }

            /// 切換 ShelfView 的 DataTemplate 並且綁定 wnd.DataView 顯示:
            if (wnd.ViewMode == MainWindow.ICONVIEW)
            {
                ShelfView.ClearValue(ItemsControl.ItemContainerStyleProperty);
                ShelfView.ItemsPanel = FindResource("IconViewTemplate") as ItemsPanelTemplate;
                ShelfView.ItemTemplate = FindResource("IconViewDataTemplate") as DataTemplate;
                ViewModeIcon.Source = DetailViewImage;
                ViewModeLabel.Text = Properties.Resources.DetailViewMode;
            }

            ShelfView.ItemsSource = wnd.DataView;
            ShelfView.UpdateLayout();
            if (wnd.SelectedAlbum != null) /// 盡可能恢復閱讀前的捲動位置
            {   ShelfView.ScrollIntoView(wnd.SelectedAlbum);  }
        }

        /// <summary>
        ///  切換縮圖模式與詳細模式。
        /// </summary>
        private void ViewModeButton_Click(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;

            if (wnd.ViewMode == MainWindow.DETAILVIEW)
            {
                wnd.ViewMode = MainWindow.ICONVIEW;
                ShelfView.ItemContainerStyle = FindResource("IconViewItemStyle") as Style;
                ShelfView.ItemsPanel = FindResource("IconViewTemplate") as ItemsPanelTemplate;
                ShelfView.ItemTemplate = FindResource("IconViewDataTemplate") as DataTemplate;

                ViewModeIcon.Source = DetailViewImage;
                ViewModeLabel.Text = Properties.Resources.DetailViewMode;
            }
            else
            {
                wnd.ViewMode = MainWindow.DETAILVIEW;
                ShelfView.ItemContainerStyle = FindResource("DetailViewItemStyle") as Style;
                ShelfView.ItemsPanel = FindResource("DetailViewTemplate") as ItemsPanelTemplate;
                ShelfView.ItemTemplate = FindResource("DetailViewDataTemplate") as DataTemplate;

                ViewModeIcon.Source = IconViewImage;
                ViewModeLabel.Text = Properties.Resources.IconViewMode;
            }

            ShelfView.UpdateLayout();
        }
        #endregion

        #region Change the working directory.
        /// ///////////////////////////////////////////////////////////////////////////////////////
        private async void SetNewWorkingDirectory(String newWorkDir)
        {
            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;

            /// 以子執行緒刪除舊資料:
            ImportProgBar.IsIndeterminate = true;
            MessageLabel.Text = Properties.Resources.ClearCacheMsg;
            await Task.Run(() => app.ClearAlbumCache(true));

            /// 以子執行緒載入新資料:
            app.WorkDir = newWorkDir;
            WorkDirTextBox.Text = app.WorkDir;
            MessageLabel.Text = Properties.Resources.RefreshingMsg;

            await Task.Run(() => app.GetAlbumsInWorkDir());
            ImportProgBar.IsIndeterminate = false;

            if (String.IsNullOrEmpty(app.LastError) == false) {  MessageLabel.Text = app.LastError;  }
            else {  MessageLabel.Text = Properties.Resources.WelcomeMsg;  }

            /// 將 app.Albums 複製到 wnd.DataView 並依選項排序之:
            wnd.FileSearch = String.Empty;
            TokenTextBox.Text = String.Empty;

            if (app.Albums != null)
            {   wnd.DataView = new List<AlbumInfo> (app.Albums);  }
            else {  wnd.DataView = new List<AlbumInfo>();  }

            wnd.SortDataView();
            ShelfView.ItemsSource = wnd.DataView;
            wnd.SelectedAlbum = null;
        }

        /// <summary>
        ///  按下 Enter 就切換工作目錄。
        /// </summary>
        private void WorkDirTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                App app = Application.Current as App;
                String newWorkDir = WorkDirTextBox.Text;
                if (String.IsNullOrEmpty(newWorkDir) == true) {  return;  }
                if (String.Compare(newWorkDir, app.WorkDir, true) == 0) {  return;  }
                if (Directory.Exists(newWorkDir) == false)
                {   MessageBox.Show(Properties.Resources.DirectoryNotExist, Properties.Resources.Error);  return;  }

                SetNewWorkingDirectory(newWorkDir);
            }
        }

        /// <summary>
        ///  透過 FolderBrowserDialog 選擇新的工作目錄。
        /// </summary>
        private void BrowseDirButton_Click(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;
            System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.SelectedPath = app.WorkDir;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                String newWorkDir = dialog.SelectedPath;

                if (String.IsNullOrEmpty(newWorkDir) == true) {  return;  }
                if (String.Compare(newWorkDir, app.WorkDir, true) == 0) {  return;  }
                if (Directory.Exists(newWorkDir) == false)
                {   MessageBox.Show(Properties.Resources.DirectoryNotExist, Properties.Resources.Error);  return;  }

                SetNewWorkingDirectory(newWorkDir);
            }
        }
        #endregion

        #region Search albums by title or filename.
        /// ///////////////////////////////////////////////////////////////////////////////////////
        /// 對目前的相簿清單進行過濾。
        private void TokenTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {   SearchButton_Click(null, null);  }
        }

        /// <summary>
        ///  根據 TokenTextBox 的文字過濾 Albums 並形成新的 DataView。
        /// </summary>
        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;
            if (String.IsNullOrEmpty(TokenTextBox.Text) == true) {  return;   }
            wnd.FileSearch = TokenTextBox.Text.Trim();
            wnd.DataView = new List<AlbumInfo>();

            if (app.Albums != null)
            {
                foreach (AlbumInfo info in  app.Albums)
                {
                    if (info.Title.IndexOf(wnd.FileSearch, StringComparison.OrdinalIgnoreCase) != -1)
                    {   wnd.DataView.Add(info);  }
                    else if (info.FileName.IndexOf(wnd.FileSearch, StringComparison.OrdinalIgnoreCase) != -1)
                    {   wnd.DataView.Add(info);  }
                    else if (info.Author.IndexOf(wnd.FileSearch, StringComparison.OrdinalIgnoreCase) != -1)
                    {   wnd.DataView.Add(info);  }
                    else if (info.Location.IndexOf(wnd.FileSearch, StringComparison.OrdinalIgnoreCase) != -1)
                    {   wnd.DataView.Add(info);  }
                }
            }

            wnd.SortDataView();
            ShelfView.ItemsSource = wnd.DataView;
            wnd.SelectedAlbum = null;
        }

        /// <summary>
        ///  重置搜尋狀態。
        /// </summary>
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;
            wnd.FileSearch = String.Empty;
            TokenTextBox.Text = String.Empty;

            /// 將 app.Albums 複製到 wnd.DataView 並依選項排序之:
            if (app.Albums != null)
            {   wnd.DataView = new List<AlbumInfo> (app.Albums);  }
            else {  wnd.DataView = new List<AlbumInfo>();  }

            wnd.SortDataView();
            ShelfView.ItemsSource = wnd.DataView;
            wnd.SelectedAlbum = null;
        }
        #endregion

        #region Select an album and read it in ReadPage.
        /// ///////////////////////////////////////////////////////////////////////////////////////
        private void ReadAlbum(AlbumInfo info)
        {
            App app = Application.Current as App;
            String pathName = Path.Combine(app.WorkDir, info.FileName);

            if (File.Exists(pathName) == false)
            {   MessageBox.Show(Properties.Resources.FileNotFound, Properties.Resources.Error);  return;  }

            if (info.Body == null)
            {
                Album album = new Album();
                album.PathName = pathName;

                MessageLabel.Text = Properties.Resources.AnalyzingEpubMsg;
                if (album.LoadXml() == false) {  MessageLabel.Text = album.LastError;  return;  }
                info.Body = album;
            }

            ReaderPage page = new ReaderPage();
            page.Album = info.Body;
            NavigationService.Navigate(page);
        }

        /// 前往 ReadPage 閱讀指定的相簿檔案。
        private void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;

            if (wnd.SelectedAlbum != null) {  ReadAlbum(wnd.SelectedAlbum);  }
            else
            {   MessageBox.Show(Properties.Resources.SelectFileMsg, Properties.Resources.Error);  }            
        }

        /// <summary>
        ///  把選定的相簿資訊顯示在右側。
        /// </summary>
        private void ShelfView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;

            if (ShelfView.SelectedItem != null)
            {
                wnd.SelectedAlbum = ShelfView.SelectedItem as AlbumInfo;
                AlbumInfoView.DataContext = wnd.SelectedAlbum;
            }
            else
            {
                wnd.SelectedAlbum = null;
                AlbumInfoView.DataContext = null;
            }
        }

        /// <summary>
        ///  在相簿項目上按下 Enter 也是閱讀該相簿。
        /// </summary>
        private void ShelfViewItem_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ListViewItem item = sender as ListViewItem;
                AlbumInfo info = item.DataContext as AlbumInfo;
                e.Handled = true;

                ReadAlbum(info);
            }
        }

        /// <summary>
        ///  不管是縮圖模式還是條列模式，雙擊項目的時候都開啟 ReaderPage 閱讀之。
        /// </summary>
        private void ShelfViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount > 1)
            {
                ListViewItem item = sender as ListViewItem;
                AlbumInfo info = item.DataContext as AlbumInfo;
                e.Handled = true;

                ReadAlbum(info);
            }
        }

        /// <summary>
        ///  使用檔案對話框瀏覽單獨的 .album.epub 檔案並閱讀之。
        /// </summary>
        private void BrowseFileButton_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog();
            dialog.Title = Properties.Resources.BrowseFileMsg;
            dialog.Filter = "Album Epub files (*.epub)|*.epub";
            dialog.RestoreDirectory = true;
            dialog.CheckFileExists = true;
            dialog.Multiselect = false;

            /// 開啟檔案對話框，選取文件檔案:
            System.Windows.Forms.DialogResult result = dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                Album album = new Album();
                album.PathName = dialog.FileName;

                MessageLabel.Text = Properties.Resources.AnalyzingEpubMsg;
                if (album.LoadXml() == false) {  MessageLabel.Text = album.LastError;  return;  }

                ReaderPage page = new ReaderPage();
                page.Album = album;
                NavigationService.Navigate(page);
            }
        }
        #endregion

        #region Sort the album list.
        /// <summary>
        ///  開啟對話框選擇排序方式。
        /// </summary>
        private void SortButton_Click(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = SortButton.ContextMenu;
            menu.PlacementTarget = SortButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void SortOption_Click(object sender, RoutedEventArgs e)
        {
            App app = Application.Current as App;
            MainWindow wnd = app.MainWindow as MainWindow;

            if (sender != null)
            {
                MenuItemByDate.IsChecked = false;
                MenuItemByTitle.IsChecked = false;
                MenuItemByAuthor.IsChecked = false;
                MenuItemByFilename.IsChecked = false;

                if (sender.Equals(MenuItemByDate)) {  wnd.SortOption = MainWindow.ORDER_BY_DATE;  }
                else if (sender.Equals(MenuItemByTitle)) {  wnd.SortOption = MainWindow.ORDER_BY_TITLE; }
                else if (sender.Equals(MenuItemByAuthor)){  wnd.SortOption = MainWindow.ORDER_BY_AUTHOR;  }
                else if (sender.Equals(MenuItemByFilename)) {  wnd.SortOption = MainWindow.ORDER_BY_FILENAME;  }

                if (wnd.SortOption == MainWindow.ORDER_BY_DATE) {  MenuItemByDate.IsChecked = true; }
                else if (wnd.SortOption == MainWindow.ORDER_BY_TITLE) {  MenuItemByTitle.IsChecked = true;  }
                else if (wnd.SortOption == MainWindow.ORDER_BY_AUTHOR) {  MenuItemByAuthor.IsChecked = true;  }
                else if (wnd.SortOption == MainWindow.ORDER_BY_FILENAME) {  MenuItemByFilename.IsChecked = true;  }

                if (sender.Equals(MenuItemAscending)) {  wnd.IsAscending = true;  }
                else if (sender.Equals(MenuItemDescending)) {  wnd.IsAscending = false;  }

                if (wnd.IsAscending == true)
                {
                    MenuItemAscending.IsChecked = true;
                    MenuItemDescending.IsChecked = false;
                }
                else if (wnd.IsAscending == false)
                {
                    MenuItemAscending.IsChecked = false;
                    MenuItemDescending.IsChecked = true;
                }
            }

            wnd.SortDataView();
            ShelfView.ItemsSource = wnd.DataView;
        }
        #endregion

        #region Go to another page.
        /// <summary>
        ///  前往 FinderPage 進行相簿內尋找段落。
        /// </summary>
        private void FindButton_Click(object sender, RoutedEventArgs e)
        {   NavigationService.Navigate(new FinderPage());  }

        /// <summary>
        ///  開啟預設瀏覽器閱讀線上說明。
        /// </summary>
        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {   Process.Start(Properties.Resources.HelpUrl);  }
        #endregion
    }
}
