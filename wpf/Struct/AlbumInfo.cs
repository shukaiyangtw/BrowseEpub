/** @file AlbumInfo.cs
 *  @brief 相簿基本資訊

 *  這個資料結構只用於 AlbumListPage 綁定顯示而已，完整相簿資料結構請見 Album.cs。

 *  @author Shu-Kai Yang (skyang@csie.nctu.edu.tw)
 *  @date 2024/5/7 */

using System;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BrowseEpub
{
    public sealed class AlbumInfo : IComparable<AlbumInfo>
    {
        /// <summary>
        ///  已載入的相簿資料結構。
        /// </summary>
        public Album Body = null;
    
        /// <summary>
        ///  相簿的唯一識別碼，通常是個字串化的 UUID。
        /// </summary>
        public String Identifier = String.Empty;

        #region Bindable properties: Title, Author, Location, FirstDate, LastDate, FileName, FileSize
        /// ///////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///  相簿標題。
        /// </summary>
        private String m_title = "(Untitled)";
        public String Title
        {
            get {  return m_title;  }
            set {  m_title = value;  }
        }

        /// <summary>
        ///  相簿作者。
        /// </summary>
        private String m_author = String.Empty;
        public String Author
        {
            get {  return m_author;  }
            set {  m_author = value;  }
        }

        /// <summary>
        ///  拍攝地點。
        /// </summary>
        private String m_location = String.Empty;
        public String Location
        {
            get {  return m_location;  }
            set {  m_location = value;  }
        }

        /// <summary>
        ///  拍照日期第一天與最後一天。
        /// </summary>
        private DateTime m_firstDate;
        private DateTime m_lastDate;

        public DateTime FirstDate
        {
            get {  return m_firstDate;  }
            set
            {
                m_firstDate = value;
                m_lastDate = m_firstDate;
            }
        }

        public DateTime LastDate
        {
            get {  return m_lastDate;  }
            set {  m_lastDate = value;  }
        }

        /// <summary>
        ///  原始檔名和尺寸(不含路徑)。
        /// </summary>
        private String m_fileName = String.Empty;
        public String FileName
        {
            get {  return m_fileName;  }
            set {  m_fileName = value;  }
        }

        private Int64 m_fileSize = 0L;
        public Int64 FileSize
        {
            get {  return m_fileSize;  }
            set {  m_fileSize = value;  }
        }

        /// 紀錄相簿封面檔案在壓縮檔內的檔名，以便能夠快速取出:
        public String CoverFileName = String.Empty;
        #endregion

        #region Read-only properties: DateStr, FileSizeStr.
        /// <summary>
        ///  統一的日期字串顯示格式。 
        /// </summary>
        static public readonly String DateFormat = "MMM dd, yyyy";

        /// <summary>
        ///  用於顯示相簿日期的唯讀屬性。
        /// </summary>
        public String DateStr
        {
            get
            {
                String str1 = m_firstDate.ToString(DateFormat);
                if (m_firstDate.Date.Equals(m_lastDate.Date)) {  return str1;  }

                String str2 = m_lastDate.ToString(DateFormat);
                return str1 + " - " + str2;
            }
        }

        public String DateStrShort
        {
            get
            {
                String str1 = m_firstDate.ToString("d");
                if (m_firstDate.Date.Equals(m_lastDate.Date)) {  return str1;  }

                String str2 = m_lastDate.ToString("d");
                return str1 + " - " + str2;
            }
        }

        /// <summary>
        ///  用於檔案尺寸的唯讀屬性。
        /// </summary>
        public String FileSizeStr
        {
            get
            {
                String str = String.Empty;

                if (m_fileSize > 1073741824)
                {
                    double gbytes = (double)m_fileSize / 1073741824.0;
                    str = gbytes.ToString("f2") + " GB";
                }
                else if (m_fileSize > 1048576)
                {
                    double mbytes = (double)m_fileSize / 1048576.0;
                    str = mbytes.ToString("f2") + " MB";
                }
                else if (m_fileSize > 1024)
                {
                    long kbytes = (long)(m_fileSize / 1024);
                    str = kbytes.ToString() + " KB";
                }
                else
                {   str = m_fileSize.ToString() + " bytes";  }

                return str;
            }
        }
        #endregion

        #region Bindable properties: ThumbImageSrc, CoverImageSrc
        private byte[] m_thumbBytes = null;
        public byte[] ThumbBytes {  set { m_thumbBytes = value; }  }

        public BitmapImage ThumbImageSrc
        {
            get
            {
                /// 檢查縮圖檔案是否存在，存在的話將檔案資料(未解壓)複製到記憶體當中，以便多次使用:
                App app = Application.Current as App;
                if (m_thumbBytes == null)
                {
                    String pathName = Path.Combine(app.ThumbDir, Identifier + ".jpg");
                    if (File.Exists(pathName))
                    {   m_thumbBytes = File.ReadAllBytes(pathName);  }
                }

                /// 從記憶體中的檔案資料建立串流，並解壓縮獲得影像:
                if (m_thumbBytes != null)
                {
                    Stream stream = new MemoryStream(m_thumbBytes);
                    BitmapImage img = new BitmapImage();
                    img.CacheOption = BitmapCacheOption.None;
                    img.BeginInit();
                    img.StreamSource = stream;
                    img.EndInit();
                    return img;
                }

                return AlbumInfo.DefaultThumb;
            }
        }

        /// <summary>
        ///  試圖從相簿壓縮檔中取得封面圖片檔案
        /// </summary>
        public BitmapImage CoverImageSrc
        {
            get
            {
                App app = Application.Current as App;

                if (String.IsNullOrEmpty(CoverFileName) == false)
                {
                    String pathName = Path.Combine(app.WorkDir, FileName);
                    using (FileStream fileStream = File.OpenRead(pathName))
                    {
                        using (ZipArchive archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                        {
                            ZipArchiveEntry entry = archive.GetEntry("EPUB/" + CoverFileName);
                            if (entry != null)
                            {
                                using (Stream stream = entry.Open())
                                {
                                    /// 解碼之前先將影像資料複製到記憶體當中:
                                    MemoryStream ms = new MemoryStream();
                                    stream.CopyTo(ms);
                                    stream.Close();
                                    ms.Seek(0, SeekOrigin.Begin);

                                    /// 解壓縮封面影像為 BitmapImage 並且傳回之:
                                    BitmapImage img = new BitmapImage();
                                    img.CacheOption = BitmapCacheOption.None;
                                    img.BeginInit();
                                    img.StreamSource = ms;
                                    img.EndInit();
                                    return img;
                                }
                            }
                        }
                    }
                }

                return AlbumInfo.DefaultCoverImage;
            }
        }

        /// <summary>
        ///  共用的資源。
        /// </summary>
        static public BitmapImage DefaultThumb = null;
        static public BitmapImage DefaultCoverImage = null;
        #endregion

        /// 紀錄讀寫過程中最後的錯誤訊息:
        static public String LastError = String.Empty;

        /// <summary>
        ///  初始化所有的成員，但是不包括產生 UUID。
        /// </summary>
        public AlbumInfo()
        {
            m_firstDate = DateTime.Now.Date;
            m_lastDate = m_firstDate;
        }

        /// <summary>
        ///  為了要讓 List<AlbumInfo> 能夠進行 Sort 與 BinarySearch，必須實作此介面。
        /// </summary>
        int IComparable<AlbumInfo>.CompareTo(AlbumInfo other)
        {   return String.Compare(this.FileName, other.FileName, true);  }
    }
}
