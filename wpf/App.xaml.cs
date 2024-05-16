/** @file App.xaml.cs
 *  @brief 全域屬性

 *  在此宣告全域的資料物件，並且取得或建立必須的資料夾路徑。在畫面上顯示大量相簿清單的時候，不可能到
    時候才去 unzip 相簿檔案內部的album.xml，而是指定工作目錄的時候就預先擷取了所有相簿的資訊另外存成
    albums.xml 並與該目錄內容保持更新，其中 albums.xml 的檔案格式如下：

    [xml version="1.0" encoding="utf-8">
    [albums dir="工作目錄">
        [album identifier="uuid" file="檔案名稱" size="檔案尺寸">
            <title>相簿標題</title>
            <firstdate>拍照日期第一天</firstdate>
            <lastdate>拍照日期最後一天</lastdate>
            <author>拍攝者或相簿主角</author>
            <location>拍照地點</location>
            <cover>封面檔名</cover>
        </album>
    </albums>

 *  @author Shu-Kai Yang (skyang@csie.nctu.edu.tw)
 *  @date 2024/5/5 */

using System;
using System.Xml;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace BrowseEpub
{
    public partial class App : Application
    {
        /// <summary>
        ///  本程式的資料根目錄，指向 AppData/Local/AlbumEpubCache。
        /// </summary>
        private String m_dataDir = String.Empty;
        public String DataDir {  get { return m_dataDir; }  }

        /// 單純是 DataDir 下的 Thumbs 子目錄。
        private String m_thumbDir = String.Empty;
        public String ThumbDir {  get { return m_thumbDir; }  }

        /// <summary>
        ///  使用者相簿檔案所在的目錄，預設 My Documents/Album Epubs。
        /// </summary>
        public String WorkDir = String.Empty;

        /// <summary>
        ///  把 WorkDir 中所有的相簿資訊從 albums.xml 讀取到此，並依檔名排序以便進行 binary search。
        /// </summary>
        public List<AlbumInfo> Albums = null;

        /// 執行時期選項:
        public String ArgPathName = String.Empty;
        public Boolean ForcedEnUs = false;
        public Boolean ClearOnExit = false;

        /// <summary>
        ///  實現多執行緒的偵測與中止。
        /// </summary>
        public Boolean ThreadIsRunning = false;
        public Boolean CancelTheThread = false;

        /// 紀錄 albums.xml 讀寫過程中最後的錯誤訊息:
        public String LastError = String.Empty;

        /// <summary>
        ///  程式剛啟動的時候接受命令列參數。
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Debug.WriteLine("App.OnStartup()...");

            /// 取得或建立資料根目錄:
            String localAppPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            m_dataDir = String.Format(@"{0}\AlbumEpubCache", localAppPath);
            if (Directory.Exists(m_dataDir) == false) {  Directory.CreateDirectory(m_dataDir);  }
            Debug.WriteLine("Data path: " + m_dataDir);

            m_thumbDir = Path.Combine(m_dataDir, "Thumbs");
            if (Directory.Exists(m_thumbDir) == false) {  Directory.CreateDirectory(m_thumbDir);  }

            /// 載入共用的影像資源:
            AlbumInfo.DefaultThumb = new BitmapImage(new Uri("Assets/cover_thumb.jpg", UriKind.Relative));
            AlbumInfo.DefaultCoverImage = new BitmapImage(new Uri("Assets/cover_none.jpg", UriKind.Relative));

            /// 解析命令列參數，基於測試目的，可將整個程式切換成英文版:
            String[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; ++i)
            {
                String arg = args[i].ToLower();
                if (arg.Equals("-en"))                 /// 將整個程式切換成英文版
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
                    FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                        new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
                    ForcedEnUs = true;
                }
                if (arg.Equals("-clear"))                 /// 要求程式結束時清除快取
                {   ClearOnExit = true;  }
                else if (arg.EndsWith(".album.epub"))  /// 準備載入命令列指定的檔案
                {   ArgPathName = args[i];  }
            }
        }

        /// ///////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///  這個非同步函式在 AlbumListPage 一開始或更換工作目錄 WorkDir 的時候被使用，由於本軟體會
        ///  把工作目錄裡的 .album.epub 檔案之相簿資訊快取為 m_dataDir/albums.xml，並把相簿縮圖快取
        ///  在 m_dataDir/Thumbs 目錄哩，理想狀態下 WorkDir 裡的 .album.epub 檔案資訊和從 albums.xml
        ///  讀取到的 app.Albums 應該是完全一致的，實際上 WorkDir 裡的檔案可能會新增、刪除、更新以至
        ///  於 albums.xml 需要連帶地更新，同時又應避免重複解析一致的檔案以免浪費時間。

        ///  因此更新程序如下，從 albums.xml 載入的 AlbumInfo 資料先不指定給 Albums 而是存成 loaded，
        ///  然後列舉 WorkDir 中的 .album.epub 檔案，利用 binary search 逐一和 loaded 中的項目比對檔
        ///  名和尺寸，如果有符合者表示 AlbumInfo 項目是有效的，將它們移置 Albums。

        ///   在這個過程中，loaded 剩餘的項目就是不存在對應檔案或是長度不一致的舊項目，應該刪除。除
        ///   此之外，對於每個 .album.epub 檔案如果 binary search 找不到對應的項目，就是應該新增的，
        ///   於是為它們建立 AlbumInfo 項目放在 added 當中。

        ///   接下來為 added 中每個檔案讀取壓縮檔中的 EPUB/album.xml，若成功讀取則將它移置到 Albums
        ///   並且建立縮圖，最後將更新的 Albums 重新儲存成 albums.xml 檔案。
        /// </summary>
        public Task GetAlbumsInWorkDir()
        {
            Boolean albumsIsModified = false;
            LastError = String.Empty;
            ThreadIsRunning = true;
            Debug.WriteLine("app.GetAlbumsInWorkDir(" + WorkDir + ")...");

            #region Read albums.xml and create an AlbumInfo list "loaded".
            /// 先嘗試由 m_dataDir 解讀 albums.xml，以載入既有的 AlbumInfo 資料，若檔案不存在或無法
            /// 解讀則放棄載入但可繼續程序，將會重建所有的 AlbumInfo 資料再次儲存為 albums.xml 檔案。
            List<AlbumInfo> loaded = new List<AlbumInfo>();
            String xmlPathName = Path.Combine(m_dataDir, "albums.xml");
            if (File.Exists(xmlPathName))
            {
                XmlDocument doc = new XmlDocument();
                Debug.WriteLine("Reading " + xmlPathName + "...");
                try {   doc.Load(xmlPathName);  }
                catch (Exception ex)
                {   doc = null;  LastError = ex.Message;  Debug.WriteLine(LastError);  }

                if (doc != null)
                {
                    /// 解讀 XmlDocument 並且建構 AlbumInfo 物件:
                    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                    {
                        if (node.NodeType == XmlNodeType.Element)
                        {
                            XmlElement element = node as XmlElement;
                            if (element.Name.Equals("album"))
                            {
                                AlbumInfo info = new AlbumInfo();
                                info.Identifier = element.GetAttribute("identifier");
                                info.FileName = element.GetAttribute("file");
                                info.FileSize = Int64.Parse(element.GetAttribute("size"));
                                Debug.WriteLine("album " + info.FileName + ": id=" + info.Identifier + ", size = " + info.FileSizeStr);

                                foreach (XmlNode child in element.ChildNodes)
                                {
                                    if (child.NodeType == XmlNodeType.Element)
                                    {
                                        XmlElement ele = child as XmlElement;
                                        if (ele.Name.Equals("title"))         {  info.Title = ele.InnerText;  }
                                        else if (ele.Name.Equals("location")) {  info.Location = ele.InnerText;  }
                                        else if (ele.Name.Equals("author"))   {  info.Author = ele.InnerText;  }
                                        else if (ele.Name.Equals("cover"))    {  info.CoverFileName = ele.InnerText;  }
                                        else if (ele.Name.Equals("firstdate"))
                                        {
                                            try
                                            {
                                                DateTime dt = DateTime.Parse(ele.InnerText);
                                                info.FirstDate = dt;
                                            }
                                            catch (Exception ex)
                                            {   MessageBox.Show(ex.Message);  }
                                        }
                                        else if (ele.Name.Equals("lastdate"))
                                        {
                                            try
                                            {
                                                DateTime dt = DateTime.Parse(ele.InnerText);
                                                info.LastDate = dt;
                                            }
                                            catch (Exception ex)
                                            {   MessageBox.Show(ex.Message);  }
                                        }
                                    }
                                }

                                /// 把 album info 加入到暫時的 collection 當中:
                                loaded.Add(info);
                            }
                        }
                    }

                    Debug.WriteLine("Albums in albums.xml: " + loaded.Count);
                }
            }
            else
            {   Debug.WriteLine("albums.xml not found!");  }
            #endregion

            #region Get *.album.epub files in WorkDir and compare them with the list.
            /// ///////////////////////////////////////////////////////////////////////////////////
            /// 列出 WorkDir 下所有的 .album.epub 檔案，並且檢查與 albums.xml 中的項目是否一致。
            DirectoryInfo directoryInfo = null;
            FileInfo[] files = null;

            if (Directory.Exists(WorkDir) == false)
            {
                try {  Directory.CreateDirectory(WorkDir);  }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    Debug.WriteLine(LastError);
                    ThreadIsRunning = false;
                    return Task.CompletedTask;
                }
            }

            try
            {
                directoryInfo = new DirectoryInfo(WorkDir);
                files = directoryInfo.GetFiles("*.album.epub", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Debug.WriteLine(LastError);
                ThreadIsRunning = false;
                return Task.CompletedTask;
            }

            Debug.WriteLine("Files found in " + WorkDir + ": " + files.Length);

            /// 重建 Albums 集合，對於 WorkDir 裡面的每個 .album.epub 檔案，檢查 loaded 中是否有對應
            /// 的項目 info 存在，若有則直接把它移置 Albums 中，若否(或是檔案尺寸不同)，則在 Albums
            /// 建立空白的新項目，並等待清除舊項目。
            List<AlbumInfo> added = new List<AlbumInfo>();
            Albums = new List<AlbumInfo>();

            foreach (FileInfo file in files)
            {
                AlbumInfo album = new AlbumInfo();
                album.FileName = file.Name;
                album.FileSize = file.Length;

                int i = loaded.BinarySearch(album);
                if (i < 0)
                {   added.Add(album);  }
                else
                {
                    AlbumInfo info = loaded[i];
                    if (info.FileSize == file.Length)
                    {
                        Albums.Add(info);
                        loaded.RemoveAt(i);
                     /* Debug.WriteLine("Confirmed album file: " + info.FileName); */
                    }
                    else
                    {   added.Add(album);  }
                }
            }

            /// 每當在 files 中找到一個既有項目的時候，就會從 loaded 中移除之，因此在此輪過後還留在
            /// loaded 中的，都是應該要刪除的過時項目。
            foreach (AlbumInfo info in loaded)
            {
                String cachePath = Path.Combine(m_dataDir, info.Identifier);
                if (Directory.Exists(cachePath) == true)
                {   try {  Directory.Delete(cachePath, true);  } catch { }  }

                String pathName = Path.Combine(m_thumbDir, info.Identifier + ".jpg");
                if (File.Exists(pathName) == true)
                {   try {  File.Delete(pathName);  } catch { }  }

                Debug.WriteLine("Deleted album id: " + info.Identifier);
                albumsIsModified = true;
            }
            #endregion

            #region Extract EPUB/album.xml and the cover image from .album.epub files.
            /// ///////////////////////////////////////////////////////////////////////////////////
            /// 從每個待加入的新 .album.epub 檔案中解壓縮 EPUB/album.xml 檔案並解讀之。
            foreach (AlbumInfo info in added)
            {
                ZipArchive archive = null;
                FileStream fileStream = null;
                Debug.WriteLine("Adding album file: " + info.FileName + "...");

                /// 嘗試以 ZipArchive 開啟 file，如果不是有效的 zip 檔案則略過:
                String pathName = Path.Combine(WorkDir, info.FileName);
                try
                {
                    fileStream = File.OpenRead(pathName);
                    archive = new ZipArchive(fileStream, ZipArchiveMode.Read);
                }
                catch (Exception ex)
                {
                    archive = null;
                    LastError = ex.Message;
                    Debug.WriteLine(LastError);
                }

                if (archive != null)
                {
                    /// 檢查 EPUB/album.xml 是否存在，藉以判斷它是否為相簿檔案:
                    ZipArchiveEntry entry = archive.GetEntry("EPUB/album.xml");
                    if (entry != null)
                    {
                        using (Stream stream = entry.Open())
                        {
                            XmlDocument doc = new XmlDocument();
                            try { doc.Load(stream); }
                            catch (Exception ex)
                            {
                                doc = null;
                                LastError = ex.Message;
                                Debug.WriteLine(LastError);
                            }

                            /// 從 ZipArchiveEntry 解壓縮的串流中解讀 album.xml 檔案:
                            if (doc != null)
                            {
                                info.Identifier = doc.DocumentElement.GetAttribute("identifier");

                                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                                {
                                    if (node.NodeType == XmlNodeType.Element)
                                    {
                                        XmlElement element = node as XmlElement;
                                        if (element.Name.Equals("title")) { info.Title = element.InnerText; }
                                        else if (element.Name.Equals("firstdate"))
                                        {
                                            DateTime dt = DateTime.Parse(element.InnerText);
                                            info.FirstDate = dt;
                                        }
                                        else if (element.Name.Equals("lastdate"))
                                        {
                                            DateTime dt = DateTime.Parse(element.InnerText);
                                            info.LastDate = dt;
                                        }
                                        else if (element.Name.Equals("location")) { info.Location = element.InnerText; }
                                        else if (element.Name.Equals("author")) { info.Author = element.InnerText; }
                                        else if (element.Name.Equals("cover")) { info.CoverFileName = element.InnerText; }
                                    }
                                }

                                /// 解讀成功，將 info 加入 Albums 當中:
                                Albums.Add(info);
                                Debug.WriteLine("Album " + info.Identifier + " is added OK.");
                                albumsIsModified = true;
                            }
                        }
                    }
                    else
                    {   Debug.WriteLine("EPUB/album.xml is not found.");  }

                    /// 從 .album.epub 檔案中解壓縮封面圖檔並建立縮圖:
                    if (String.IsNullOrEmpty(info.CoverFileName) == false)
                    {
                        entry = archive.GetEntry("EPUB/" + info.CoverFileName);
                        if (entry != null)
                        {
                            using (Stream stream = entry.Open())
                            {
                                /// 解碼之前先將影像資料複製到記憶體當中:
                                MemoryStream ms = new MemoryStream();
                                stream.CopyTo(ms);
                                stream.Close();
                                ms.Seek(0, SeekOrigin.Begin);

                                /// 從記憶體串流解壓出封面影像物件:
                                Image srcImg = null;
                                try {  srcImg = Bitmap.FromStream(ms);  }
                                catch (Exception ex)
                                {
                                    srcImg = null;
                                    LastError = ex.Message;
                                    Debug.WriteLine(LastError);
                                }

                                /// 準備一個 300 x 400 的畫布:
                                Image destImg = new Bitmap(300, 400);
                                Graphics g = Graphics.FromImage(destImg);
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.High;
                                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                                g.Clear(Color.Transparent);

                                int x = 0, y = 0;
                                int width = srcImg.Width;
                                int height = srcImg.Height;

                                /// 計算並比較兩影像的長寬比，並且進行裁切:
                                Double aspect = ((double)srcImg.Width) / ((double)srcImg.Height);
                                if (aspect > 0.75)
                                {
                                    width = (int)((float)height * 0.75);
                                    x = (srcImg.Width - width) / 2;
                                }
                                else
                                {
                                    height = (int)((float)width / 0.75);
                                    y = (srcImg.Height - height) / 2;
                                }

                                /// 將 srcImg 描繪到 destImg 上:
                                g.DrawImage(srcImg,
                                    new Rectangle(0, 0, 300, 400),
                                    new Rectangle(x, y, width, height),
                                    GraphicsUnit.Pixel);

                                /// 將 destImg 另存為檔案:
                                String destPathName = Path.Combine(m_thumbDir, info.Identifier + ".jpg");
                                try { destImg.Save(destPathName, ImageFormat.Jpeg); }
                                catch (Exception ex)
                                {   LastError = ex.Message;  Debug.WriteLine(LastError);  }
                            }
                        }
                    }
                }

                if (fileStream != null) {  fileStream.Close();  }
                if (CancelTheThread == true) {  break;  }
            }
            #endregion

            #region Write albums.xml if modified.
            /// ///////////////////////////////////////////////////////////////////////////////////
            /// Albums 經過重建過更新，必須儲存 albums.xml 檔案。
            if (albumsIsModified == true)
            {
                Albums.Sort();

                XmlDocument doc = new XmlDocument();
                XmlDeclaration declaration = doc.CreateXmlDeclaration("1.0", "utf-8", null);
                doc.AppendChild(declaration);

                XmlElement root = doc.CreateElement("albums");
                doc.AppendChild(root);

                foreach (AlbumInfo info in Albums)
                {
                    /// 為每一本相簿建立 album 元素，並加入倒 xml document 的根節點:
                    XmlElement element = doc.CreateElement("album");
                    element.SetAttribute("identifier", info.Identifier);
                    element.SetAttribute("file", info.FileName);
                    element.SetAttribute("size", info.FileSize.ToString());

                    XmlElement child = doc.CreateElement("title");
                    XmlText text = doc.CreateTextNode(info.Title);
                    child.AppendChild(text);
                    element.AppendChild(child);

                    child = doc.CreateElement("firstdate");
                    String str = info.FirstDate.ToString("yyyy-MM-dd");
                    text = doc.CreateTextNode(str);
                    child.AppendChild(text);
                    element.AppendChild(child);

                    if (info.LastDate.Equals(info.FirstDate) == false)
                    {
                        child = doc.CreateElement("lastdate");
                        str = info.LastDate.ToString("yyyy-MM-dd");
                        text = doc.CreateTextNode(str);
                        child.AppendChild(text);
                        element.AppendChild(child);
                    }

                    if (String.IsNullOrWhiteSpace(info.Location) == false)
                    {
                        child = doc.CreateElement("location");
                        text = doc.CreateTextNode(info.Location);
                        child.AppendChild(text);
                        element.AppendChild(child);
                    }

                    if (String.IsNullOrWhiteSpace(info.Author) == false)
                    {
                        child = doc.CreateElement("author");
                        text = doc.CreateTextNode(info.Author);
                        child.AppendChild(text);
                        element.AppendChild(child);
                    }

                    if (String.IsNullOrWhiteSpace(info.CoverFileName) == false)
                    {
                        child = doc.CreateElement("cover");
                        text = doc.CreateTextNode(info.CoverFileName);
                        child.AppendChild(text);
                        element.AppendChild(child);
                    }

                    root.AppendChild(element);
                }

                /// 把 XML document 寫入 albums.xml:
                Debug.WriteLine("Writing " + xmlPathName + "...");

                try
                {
                    XmlWriterSettings settings = new XmlWriterSettings();
                    settings.Indent = true;

                    FileStream stream = File.Create(xmlPathName);
                    XmlWriter writer = XmlWriter.Create(stream, settings);
                    doc.Save(writer);
                    stream.Dispose();
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    Debug.WriteLine(LastError);
                }
            }
            #endregion

            CancelTheThread = false;
            ThreadIsRunning = false;
            return Task.CompletedTask;
        }

        /// <summary>
        ///  刪除 m_dataDir 中的既有內容，如果 clearAll 為 false 則保留 albums.xml 和 Thumbs。
        /// </summary>
        public Task ClearAlbumCache(Boolean clearAll = false)
        {
            LastError = String.Empty;
            ThreadIsRunning = true;

            if (clearAll == true)
            { 
                String xmlPathName = Path.Combine(m_dataDir, "albums.xml");
                if (File.Exists(xmlPathName)) {  try { File.Delete(xmlPathName);  } catch { }  }

                String[] thumbs = Directory.GetFiles(m_thumbDir);
                foreach (String pathName in thumbs)
                {
                    try {  File.Delete(pathName); } catch { }

                    if (CancelTheThread == true)
                    {
                        CancelTheThread = false;
                        ThreadIsRunning = false;
                        return Task.CompletedTask;
                    }
                }
            }

            /// 刪除先前已經解壓縮的相簿內容:
            String[] directories = Directory.GetDirectories(m_dataDir);
            foreach (String subDir in directories)
            {
                if (subDir.Equals(m_thumbDir) == false)
                {   try {  Directory.Delete(subDir, true);  } catch { }  }
                if (CancelTheThread == true) {  break;  }
            }

            CancelTheThread = false;
            ThreadIsRunning = false;
            return Task.CompletedTask;
        }
    }
}
